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
/// Phase 10.5.K — DevCardsController POST /api/dev/cards のテスト。
/// override 層に新規カードを作成する。template 指定時は当該カードの active spec を v1 にクローン。
/// </summary>
public class DevCardsControllerNewCardTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _overrideRoot;
    private readonly string _baseDir;
    private readonly string _backupRoot;
    private readonly string _dataRoot;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly System.Net.Http.HttpClient _client;

    public DevCardsControllerNewCardTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "dev-newcard-" + Guid.NewGuid().ToString("N"));
        _overrideRoot = Path.Combine(_tempRoot, "dev-overrides");
        _baseDir = Path.Combine(_tempRoot, "base");
        _backupRoot = Path.Combine(_tempRoot, "backups");
        _dataRoot = Path.Combine(_tempRoot, "data");
        Directory.CreateDirectory(_overrideRoot);
        Directory.CreateDirectory(_baseDir);
        Directory.CreateDirectory(_backupRoot);
        Directory.CreateDirectory(_dataRoot);

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
    public async Task NewCard_creates_card_with_default_spec()
    {
        var body = new
        {
            id = "new_skill_x",
            name = "新規スキル",
            displayName = (string?)null,
            templateCardId = (string?)null,
        };
        var resp = await _client.PostAsJsonAsync("/api/dev/cards", body);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        // GET で確認: new_skill_x が一覧にいる
        var listResp = await _client.GetAsync("/api/dev/cards");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        var newCard = FindCardOrNull(list, "new_skill_x");
        Assert.True(newCard.HasValue, "new_skill_x not found in /api/dev/cards");
        Assert.Equal("v1", newCard!.Value.GetProperty("activeVersion").GetString());
        var versions = newCard.Value.GetProperty("versions");
        Assert.Equal(1, versions.GetArrayLength());

        // default spec = Skill / cost 1 / effects=[]
        var specStr = versions[0].GetProperty("spec").GetString();
        Assert.NotNull(specStr);
        using var specDoc = JsonDocument.Parse(specStr!);
        var spec = specDoc.RootElement;
        Assert.Equal("Skill", spec.GetProperty("cardType").GetString());
        Assert.Equal(1, spec.GetProperty("cost").GetInt32());
        Assert.Equal(0, spec.GetProperty("effects").GetArrayLength());
    }

    [Fact]
    public async Task NewCard_clones_template_spec()
    {
        // strike は manifest 経由の base に存在 (effects[0].amount=6)
        var body = new { id = "strike_clone", name = "ストライククローン", templateCardId = "strike" };
        var resp = await _client.PostAsJsonAsync("/api/dev/cards", body);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var listResp = await _client.GetAsync("/api/dev/cards");
        var list = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        var newCard = FindCardOrNull(list, "strike_clone");
        Assert.True(newCard.HasValue);
        Assert.Equal("v1", newCard!.Value.GetProperty("activeVersion").GetString());
        var versions = newCard.Value.GetProperty("versions");
        var specStr = versions[0].GetProperty("spec").GetString();
        Assert.NotNull(specStr);
        using var doc = JsonDocument.Parse(specStr!);
        var spec = doc.RootElement;
        // strike の amount=6 が反映されている
        Assert.Equal(6, spec.GetProperty("effects")[0].GetProperty("amount").GetInt32());
    }

    [Fact]
    public async Task NewCard_id_collision_returns_409()
    {
        // strike は manifest base に存在
        var body = new { id = "strike", name = "重複", templateCardId = (string?)null };
        var resp = await _client.PostAsJsonAsync("/api/dev/cards", body);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task NewCard_invalid_id_returns_400()
    {
        var body = new { id = "Invalid-ID!", name = "x", templateCardId = (string?)null };
        var resp = await _client.PostAsJsonAsync("/api/dev/cards", body);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task NewCard_unknown_template_returns_400()
    {
        var body = new { id = "x_card", name = "x", templateCardId = "nonexistent_card" };
        var resp = await _client.PostAsJsonAsync("/api/dev/cards", body);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    private static JsonElement? FindCardOrNull(JsonElement list, string id)
    {
        foreach (var entry in list.EnumerateArray())
        {
            if (entry.GetProperty("id").GetString() == id) return entry;
        }
        return null;
    }
}

/// <summary>
/// 本番 (Production) 環境では POST /api/dev/cards も 404。
/// </summary>
public class DevCardsControllerNewCardProdTests : IClassFixture<ProductionWebApplicationFactory>
{
    private readonly ProductionWebApplicationFactory _factory;

    public DevCardsControllerNewCardProdTests(ProductionWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task NewCard_returns_404_in_production()
    {
        var client = _factory.CreateClient();
        var body = new { id = "anything", name = "x" };
        var resp = await client.PostAsJsonAsync("/api/dev/cards", body);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
