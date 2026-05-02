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
/// Phase 10.5.L1 — DevRelicsController の GET endpoint テスト。
/// </summary>
public class DevRelicsControllerTests : IClassFixture<DevWebApplicationFactory>
{
    private readonly DevWebApplicationFactory _factory;

    public DevRelicsControllerTests(DevWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task GetRelics_returns_200_in_dev_with_relic_list()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/dev/relics");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.ValueKind);

        // 少なくとも act1_start_01 (アンカー) と burning_blood (燃え盛る血) が含まれる。
        bool foundAnchor = false;
        bool foundBurning = false;
        foreach (var entry in body.EnumerateArray())
        {
            var id = entry.GetProperty("id").GetString();
            if (id == "act1_start_01") foundAnchor = true;
            if (id == "burning_blood") foundBurning = true;

            Assert.True(entry.TryGetProperty("activeVersion", out _));
            Assert.True(entry.TryGetProperty("versions", out var versions));
            Assert.Equal(JsonValueKind.Array, versions.ValueKind);
            Assert.True(versions.GetArrayLength() >= 1);
        }
        Assert.True(foundAnchor, "act1_start_01 not found in /api/dev/relics");
        Assert.True(foundBurning, "burning_blood not found in /api/dev/relics");
    }

    [Fact]
    public async Task GetRelics_anchor_has_v1_active_with_spec()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/dev/relics");
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement? anchor = null;
        foreach (var entry in body.EnumerateArray())
        {
            if (entry.GetProperty("id").GetString() == "act1_start_01")
            {
                anchor = entry;
                break;
            }
        }
        Assert.NotNull(anchor);

        Assert.Equal("v1", anchor!.Value.GetProperty("activeVersion").GetString());
        var versions = anchor.Value.GetProperty("versions");
        var v1 = versions[0];
        Assert.Equal("v1", v1.GetProperty("version").GetString());
        var spec = v1.GetProperty("spec").GetString();
        Assert.NotNull(spec);
        // Phase 10.5.L1.5: top-level "trigger" 廃止、"effects" は必ずある (空 [] でも)
        Assert.Contains("\"effects\"", spec);
        Assert.Contains("\"rarity\"", spec);
    }
}

public class DevRelicsControllerProdTests : IClassFixture<ProductionWebApplicationFactory>
{
    private readonly ProductionWebApplicationFactory _factory;

    public DevRelicsControllerProdTests(ProductionWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task GetRelics_returns_404_in_production()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/dev/relics");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}

/// <summary>
/// Phase 10.5.L1 — DevRelicsController の mutation endpoint 群のテスト。
/// WebApplicationFactory で DevRelicWriter を temp dir 向けに上書き injection する。
/// </summary>
public class DevRelicsControllerMutationTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _overrideRoot;
    private readonly string _baseDir;
    private readonly string _backupRoot;
    private readonly string _dataRoot;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly System.Net.Http.HttpClient _client;

    public DevRelicsControllerMutationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "dev-relic-mut-" + Guid.NewGuid().ToString("N"));
        _overrideRoot = Path.Combine(_tempRoot, "dev-overrides");
        _baseDir = Path.Combine(_tempRoot, "base");
        _backupRoot = Path.Combine(_tempRoot, "backups");
        _dataRoot = Path.Combine(_tempRoot, "data");
        Directory.CreateDirectory(_overrideRoot);
        Directory.CreateDirectory(_baseDir);
        Directory.CreateDirectory(_backupRoot);
        Directory.CreateDirectory(_dataRoot);

        // promote テスト用に temp baseDir に act1_start_01.json を用意 (manifest base と同等の versioned 形式)
        File.WriteAllText(Path.Combine(_baseDir, "act1_start_01.json"),
            """{ "id": "act1_start_01", "name": "アンカー", "activeVersion": "v1", "versions": [ { "version": "v1", "createdAt": "2026-05-01T00:00:00Z", "label": "original", "spec": { "rarity": 1, "trigger": "OnPickup", "description": "", "effects": [], "implemented": true } } ] }""");

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
                // 既存の DevRelicWriter 登録を temp dir 向けに上書き
                var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(DevRelicWriter));
                if (descriptor is not null) services.Remove(descriptor);
                services.AddSingleton(_ => new DevRelicWriter(overrideRoot, baseDir, backupRoot));
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
            spec = new
            {
                rarity = 1,
                trigger = "OnPickup",
                description = "tweaked",
                effects = Array.Empty<object>(),
                implemented = true,
            },
        };
        var resp = await _client.PostAsJsonAsync("/api/dev/relics/act1_start_01/versions", body);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var payload = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("v2", payload.GetProperty("newVersion").GetString());

        var overridePath = Path.Combine(_overrideRoot, "relics", "act1_start_01.json");
        Assert.True(File.Exists(overridePath));

        var listResp = await _client.GetAsync("/api/dev/relics");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        var anchor = FindRelic(list, "act1_start_01");
        Assert.Equal("v2", anchor.GetProperty("activeVersion").GetString());
        var versions = anchor.GetProperty("versions");
        Assert.True(versions.GetArrayLength() >= 2);
    }

    [Fact]
    public async Task SaveVersion_returns_404_for_unknown_relic()
    {
        var resp = await _client.PostAsJsonAsync("/api/dev/relics/no_such_relic/versions",
            new { label = "x", spec = new { } });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task SwitchActive_updates_activeVersion()
    {
        await _client.PostAsJsonAsync("/api/dev/relics/act1_start_01/versions",
            new { label = "v2", spec = new { rarity = 1, trigger = "Passive", effects = Array.Empty<object>() } });
        var resp = await _client.PatchAsJsonAsync("/api/dev/relics/act1_start_01/active",
            new { version = "v1" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var listResp = await _client.GetAsync("/api/dev/relics");
        var list = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        var anchor = FindRelic(list, "act1_start_01");
        Assert.Equal("v1", anchor.GetProperty("activeVersion").GetString());
    }

    [Fact]
    public async Task SwitchActive_returns_400_for_unknown_version()
    {
        var resp = await _client.PatchAsJsonAsync("/api/dev/relics/act1_start_01/active",
            new { version = "v99" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteVersion_removes_from_override()
    {
        await _client.PostAsJsonAsync("/api/dev/relics/act1_start_01/versions",
            new { label = "v2", spec = new { rarity = 1, trigger = "Passive", effects = Array.Empty<object>() } });
        await _client.PatchAsJsonAsync("/api/dev/relics/act1_start_01/active",
            new { version = "v1" });
        var resp = await _client.DeleteAsync("/api/dev/relics/act1_start_01/versions/v2");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var listResp = await _client.GetAsync("/api/dev/relics");
        var list = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        var anchor = FindRelic(list, "act1_start_01");
        var versions = anchor.GetProperty("versions");
        foreach (var v in versions.EnumerateArray())
        {
            Assert.NotEqual("v2", v.GetProperty("version").GetString());
        }
    }

    [Fact]
    public async Task DeleteVersion_active_returns_400()
    {
        await _client.PostAsJsonAsync("/api/dev/relics/act1_start_01/versions",
            new { label = "v2", spec = new { rarity = 1, trigger = "Passive", effects = Array.Empty<object>() } });
        var resp = await _client.DeleteAsync("/api/dev/relics/act1_start_01/versions/v2");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Promote_writes_to_base_and_removes_from_override()
    {
        var saveResp = await _client.PostAsJsonAsync("/api/dev/relics/act1_start_01/versions", new
        {
            label = "promote-me",
            spec = new { rarity = 1, trigger = "OnPickup", effects = Array.Empty<object>(), description = "promoted" },
        });
        saveResp.EnsureSuccessStatusCode();
        await _client.PatchAsJsonAsync("/api/dev/relics/act1_start_01/active", new { version = "v1" });

        var resp = await _client.PostAsJsonAsync("/api/dev/relics/act1_start_01/promote",
            new { version = "v2", makeActiveOnBase = false });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var baseJson = File.ReadAllText(Path.Combine(_baseDir, "act1_start_01.json"));
        Assert.Contains("\"v2\"", baseJson);
        Assert.Contains("promote-me", baseJson);

        var backupRelicsDir = Path.Combine(_backupRoot, "relics");
        Assert.True(Directory.Exists(backupRelicsDir));
        var backups = Directory.GetFiles(backupRelicsDir, "act1_start_01-*.json");
        Assert.NotEmpty(backups);
    }

    [Fact]
    public async Task Promote_returns_404_when_version_not_in_override()
    {
        var resp = await _client.PostAsJsonAsync("/api/dev/relics/act1_start_01/promote",
            new { version = "v99", makeActiveOnBase = false });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task NewRelic_creates_override_with_v1()
    {
        var body = new
        {
            id = "test_new_relic",
            name = "テストレリック",
            displayName = (string?)null,
            templateRelicId = (string?)null,
        };
        var resp = await _client.PostAsJsonAsync("/api/dev/relics", body);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var overridePath = Path.Combine(_overrideRoot, "relics", "test_new_relic.json");
        Assert.True(File.Exists(overridePath));

        var listResp = await _client.GetAsync("/api/dev/relics");
        var list = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        var entry = FindRelic(list, "test_new_relic");
        Assert.Equal("v1", entry.GetProperty("activeVersion").GetString());
    }

    [Fact]
    public async Task NewRelic_invalid_id_returns_400()
    {
        var resp = await _client.PostAsJsonAsync("/api/dev/relics",
            new { id = "Bad-ID!", name = "X" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task NewRelic_existing_id_returns_409()
    {
        var resp = await _client.PostAsJsonAsync("/api/dev/relics",
            new { id = "act1_start_01", name = "Dup" });
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteRelic_removes_override()
    {
        // 新規 override 作成
        await _client.PostAsJsonAsync("/api/dev/relics",
            new { id = "to_delete", name = "削除する" });
        var overridePath = Path.Combine(_overrideRoot, "relics", "to_delete.json");
        Assert.True(File.Exists(overridePath));

        var resp = await _client.DeleteAsync("/api/dev/relics/to_delete");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.False(File.Exists(overridePath));
    }

    [Fact]
    public async Task DeleteRelic_unknown_returns_404()
    {
        var resp = await _client.DeleteAsync("/api/dev/relics/no_such_relic");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Preview_returns_description_override_when_present()
    {
        var body = new
        {
            spec = new
            {
                rarity = 1,
                trigger = "OnPickup",
                description = "手書きの説明文。",
                effects = Array.Empty<object>(),
            },
        };
        var resp = await _client.PostAsJsonAsync("/api/dev/relics/preview", body);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var payload = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("手書きの説明文。", payload.GetProperty("description").GetString());
    }

    [Fact]
    public async Task Preview_falls_back_to_effects_formatter_when_description_empty()
    {
        var body = new
        {
            spec = new
            {
                rarity = 1,
                trigger = "OnPickup",
                effects = new[]
                {
                    new { action = "block", scope = "self", side = "ally", amount = 5 },
                },
            },
        };
        var resp = await _client.PostAsJsonAsync("/api/dev/relics/preview", body);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var payload = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var desc = payload.GetProperty("description").GetString();
        Assert.False(string.IsNullOrEmpty(desc));
    }

    [Fact]
    public async Task Preview_combines_manual_description_and_effects_text()
    {
        // M5: 手動 description + effects 自動文章化 を結合して返す。
        var body = new
        {
            spec = new
            {
                rarity = 1,
                trigger = "OnPickup",
                description = "迷っても、心を留めるための小さな錨。",
                effects = new[]
                {
                    new { action = "gainMaxHp", scope = "self", amount = 8 },
                },
            },
        };
        var resp = await _client.PostAsJsonAsync("/api/dev/relics/preview", body);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var payload = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var desc = payload.GetProperty("description").GetString() ?? "";
        Assert.Contains("迷っても", desc);
        Assert.Contains("最大HP +", desc);  // formatter 出力
    }

    private static JsonElement FindRelic(JsonElement list, string id)
    {
        foreach (var entry in list.EnumerateArray())
        {
            if (entry.GetProperty("id").GetString() == id) return entry;
        }
        throw new InvalidOperationException($"relic '{id}' not found");
    }
}

/// <summary>
/// 本番 (Production) 環境では mutation endpoint も 404 を返すこと。
/// </summary>
public class DevRelicsControllerMutationProdTests : IClassFixture<ProductionWebApplicationFactory>
{
    private readonly ProductionWebApplicationFactory _factory;

    public DevRelicsControllerMutationProdTests(ProductionWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task SaveVersion_returns_404_in_production()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/dev/relics/act1_start_01/versions",
            new { label = "x", spec = new { } });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task SwitchActive_returns_404_in_production()
    {
        var client = _factory.CreateClient();
        var resp = await client.PatchAsJsonAsync("/api/dev/relics/act1_start_01/active",
            new { version = "v1" });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteVersion_returns_404_in_production()
    {
        var client = _factory.CreateClient();
        var resp = await client.DeleteAsync("/api/dev/relics/act1_start_01/versions/v1");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Promote_returns_404_in_production()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/dev/relics/act1_start_01/promote",
            new { version = "v1" });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task NewRelic_returns_404_in_production()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/dev/relics",
            new { id = "x", name = "y" });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteRelic_returns_404_in_production()
    {
        var client = _factory.CreateClient();
        var resp = await client.DeleteAsync("/api/dev/relics/act1_start_01");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Preview_returns_404_in_production()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/dev/relics/preview",
            new { spec = new { } });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
