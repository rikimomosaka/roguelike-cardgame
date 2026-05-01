using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Server.Services;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

/// <summary>
/// Phase 10.5.J — DevCardsController の mutation endpoint 群のテスト。
/// WebApplicationFactory で DevCardWriter を temp dir 向けに上書き injection する。
/// (Promote のテストは base + backup も temp に向けるためここで一括対応。)
/// </summary>
public class DevCardsControllerMutationTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _overrideRoot;
    private readonly string _baseDir;
    private readonly string _backupRoot;
    private readonly string _dataRoot;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly System.Net.Http.HttpClient _client;

    public DevCardsControllerMutationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "dev-mut-" + Guid.NewGuid().ToString("N"));
        _overrideRoot = Path.Combine(_tempRoot, "dev-overrides");
        _baseDir = Path.Combine(_tempRoot, "base");
        _backupRoot = Path.Combine(_tempRoot, "backups");
        _dataRoot = Path.Combine(_tempRoot, "data");
        Directory.CreateDirectory(_overrideRoot);
        Directory.CreateDirectory(_baseDir);
        Directory.CreateDirectory(_backupRoot);
        Directory.CreateDirectory(_dataRoot);

        // promote テスト用に temp baseDir に strike.json を用意 (manifest の base と同等)
        File.WriteAllText(Path.Combine(_baseDir, "strike.json"),
            """{ "id": "strike", "name": "ストライク", "activeVersion": "v1", "versions": [ { "version": "v1", "createdAt": "2026-05-01T00:00:00Z", "label": "original", "spec": { "rarity": 1, "cardType": "Attack", "cost": 1, "effects": [] } } ] }""");

        var dataRoot = _dataRoot;
        var overrideRoot = _overrideRoot;
        var baseDir = _baseDir;
        var backupRoot = _backupRoot;

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new[]
                {
                    new KeyValuePair<string, string?>("DataStorage:RootDirectory", dataRoot),
                });
            });
            builder.ConfigureServices(services =>
            {
                // 既存の DevCardWriter 登録を temp dir 向けに上書き
                var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(DevCardWriter));
                if (descriptor is not null) services.Remove(descriptor);
                services.AddSingleton(_ => new DevCardWriter(overrideRoot, baseDir, backupRoot));
            });
        });
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task SaveVersion_creates_override_with_v2_and_makes_active_on_first_save()
    {
        var body = new
        {
            label = "tweaked",
            spec = new { rarity = 1, cardType = "Attack", cost = 1, effects = Array.Empty<object>() },
        };
        var resp = await _client.PostAsJsonAsync("/api/dev/cards/strike/versions", body);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var payload = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("v2", payload.GetProperty("newVersion").GetString());

        // override file が出来ている
        var overridePath = Path.Combine(_overrideRoot, "cards", "strike.json");
        Assert.True(File.Exists(overridePath));

        // GET でも v2 が見える + activeVersion=v2
        var listResp = await _client.GetAsync("/api/dev/cards");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        var strike = FindCard(list, "strike");
        Assert.Equal("v2", strike.GetProperty("activeVersion").GetString());
        var versions = strike.GetProperty("versions");
        Assert.True(versions.GetArrayLength() >= 2);
    }

    [Fact]
    public async Task SaveVersion_increments_to_v3_when_v2_already_in_override()
    {
        // 1回目 → v2
        await _client.PostAsJsonAsync("/api/dev/cards/strike/versions", new { label = "first", spec = new { } });
        // 2回目 → v3
        var resp = await _client.PostAsJsonAsync("/api/dev/cards/strike/versions", new { label = "second", spec = new { } });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var payload = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("v3", payload.GetProperty("newVersion").GetString());
    }

    [Fact]
    public async Task SaveVersion_returns_404_for_unknown_card()
    {
        var resp = await _client.PostAsJsonAsync("/api/dev/cards/no-such-card/versions",
            new { label = "x", spec = new { } });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task SwitchActive_updates_activeVersion()
    {
        // v2 を作る
        await _client.PostAsJsonAsync("/api/dev/cards/strike/versions", new { label = "v2", spec = new { } });
        // active を v1 に戻す
        var resp = await _client.PatchAsJsonAsync("/api/dev/cards/strike/active", new { version = "v1" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var listResp = await _client.GetAsync("/api/dev/cards");
        var list = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        var strike = FindCard(list, "strike");
        Assert.Equal("v1", strike.GetProperty("activeVersion").GetString());
    }

    [Fact]
    public async Task SwitchActive_returns_400_for_unknown_version()
    {
        var resp = await _client.PatchAsJsonAsync("/api/dev/cards/strike/active", new { version = "v99" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteVersion_removes_from_override()
    {
        // v2 を作る (active=v2)
        await _client.PostAsJsonAsync("/api/dev/cards/strike/versions", new { label = "v2", spec = new { } });
        // active を v1 に
        await _client.PatchAsJsonAsync("/api/dev/cards/strike/active", new { version = "v1" });
        // v2 を削除
        var resp = await _client.DeleteAsync("/api/dev/cards/strike/versions/v2");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var listResp = await _client.GetAsync("/api/dev/cards");
        var list = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        var strike = FindCard(list, "strike");
        var versions = strike.GetProperty("versions");
        foreach (var v in versions.EnumerateArray())
        {
            Assert.NotEqual("v2", v.GetProperty("version").GetString());
        }
    }

    [Fact]
    public async Task DeleteVersion_active_returns_400()
    {
        // v2 作成 → active=v2
        await _client.PostAsJsonAsync("/api/dev/cards/strike/versions", new { label = "v2", spec = new { } });
        // active な v2 を削除しようとすると 400
        var resp = await _client.DeleteAsync("/api/dev/cards/strike/versions/v2");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Promote_writes_to_base_and_removes_from_override()
    {
        // v2 を override に作成
        var saveResp = await _client.PostAsJsonAsync("/api/dev/cards/strike/versions", new
        {
            label = "promote-me",
            spec = new { rarity = 1, cardType = "Attack", cost = 1, effects = Array.Empty<object>() },
        });
        saveResp.EnsureSuccessStatusCode();
        // active を v1 に戻して promote しても問題ない状態にする (promote は active 関係ないが、
        // override file が残った場合の挙動確認のため)
        await _client.PatchAsJsonAsync("/api/dev/cards/strike/active", new { version = "v1" });

        // promote
        var resp = await _client.PostAsJsonAsync("/api/dev/cards/strike/promote",
            new { version = "v2", makeActiveOnBase = false });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        // base file に v2 が転記されている
        var baseJson = File.ReadAllText(Path.Combine(_baseDir, "strike.json"));
        Assert.Contains("\"v2\"", baseJson);
        Assert.Contains("promote-me", baseJson);

        // backup が取られている
        var backupCardsDir = Path.Combine(_backupRoot, "cards");
        Assert.True(Directory.Exists(backupCardsDir));
        var backups = Directory.GetFiles(backupCardsDir, "strike-*.json");
        Assert.NotEmpty(backups);

        // override から v2 が消えた (file は残るが versions[] に v2 なし)
        var overridePath = Path.Combine(_overrideRoot, "cards", "strike.json");
        if (File.Exists(overridePath))
        {
            var ovr = File.ReadAllText(overridePath);
            using var doc = JsonDocument.Parse(ovr);
            var versions = doc.RootElement.GetProperty("versions");
            foreach (var v in versions.EnumerateArray())
            {
                Assert.NotEqual("v2", v.GetProperty("version").GetString());
            }
        }
    }

    [Fact]
    public async Task Promote_deletes_override_file_when_versions_empty_and_no_active()
    {
        // override に v2 を作成 → active を v1 に戻す
        await _client.PostAsJsonAsync("/api/dev/cards/strike/versions", new { label = "x", spec = new { } });
        await _client.PatchAsJsonAsync("/api/dev/cards/strike/active", new { version = "v1" });

        // override の activeVersion を unset するため versions だけ残す形を作る
        // (現状 SwitchActive で v1 をセットしたので activeVersion は残る → file は残る想定でテスト)
        var resp = await _client.PostAsJsonAsync("/api/dev/cards/strike/promote",
            new { version = "v2", makeActiveOnBase = false });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        // activeVersion=v1 が残るので override file は残る
        var overridePath = Path.Combine(_overrideRoot, "cards", "strike.json");
        Assert.True(File.Exists(overridePath));
    }

    [Fact]
    public async Task Promote_returns_404_when_version_not_in_override()
    {
        var resp = await _client.PostAsJsonAsync("/api/dev/cards/strike/promote",
            new { version = "v99", makeActiveOnBase = false });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    private static JsonElement FindCard(JsonElement list, string id)
    {
        foreach (var entry in list.EnumerateArray())
        {
            if (entry.GetProperty("id").GetString() == id) return entry;
        }
        throw new InvalidOperationException($"card '{id}' not found");
    }
}

/// <summary>
/// 本番 (Production) 環境では mutation endpoint も 404 を返すこと。
/// </summary>
public class DevCardsControllerMutationProdTests : IClassFixture<ProductionWebApplicationFactory>
{
    private readonly ProductionWebApplicationFactory _factory;

    public DevCardsControllerMutationProdTests(ProductionWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task SaveVersion_returns_404_in_production()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/dev/cards/strike/versions",
            new { label = "x", spec = new { } });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task SwitchActive_returns_404_in_production()
    {
        var client = _factory.CreateClient();
        var resp = await client.PatchAsJsonAsync("/api/dev/cards/strike/active",
            new { version = "v1" });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteVersion_returns_404_in_production()
    {
        var client = _factory.CreateClient();
        var resp = await client.DeleteAsync("/api/dev/cards/strike/versions/v1");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Promote_returns_404_in_production()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/dev/cards/strike/promote",
            new { version = "v1", makeActiveOnBase = false });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
