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
/// Phase 10.5.M — POST /api/dev/cards/preview と DELETE /api/dev/cards/{id} のテスト。
/// preview: spec を CardTextFormatter で auto-text 化して返す。
/// delete: override only / alsoBase で base file も backup → 削除。
/// </summary>
public class DevCardsPreviewTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _overrideRoot;
    private readonly string _baseDir;
    private readonly string _backupRoot;
    private readonly string _dataRoot;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly System.Net.Http.HttpClient _client;

    public DevCardsPreviewTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "dev-prev-" + Guid.NewGuid().ToString("N"));
        _overrideRoot = Path.Combine(_tempRoot, "dev-overrides");
        _baseDir = Path.Combine(_tempRoot, "base");
        _backupRoot = Path.Combine(_tempRoot, "backups");
        _dataRoot = Path.Combine(_tempRoot, "data");
        Directory.CreateDirectory(_overrideRoot);
        Directory.CreateDirectory(_baseDir);
        Directory.CreateDirectory(_backupRoot);
        Directory.CreateDirectory(_dataRoot);

        // delete テスト用に base file を準備
        File.WriteAllText(Path.Combine(_baseDir, "deletable_card.json"),
            """{ "id": "deletable_card", "name": "削除対象", "activeVersion": "v1", "versions": [ { "version": "v1", "spec": { "rarity": 1, "cardType": "Skill", "cost": 1, "effects": [] } } ] }""");

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
    public async Task Preview_returns_formatted_description_with_markers()
    {
        var body = new
        {
            spec = new
            {
                rarity = 1,
                cardType = "Attack",
                cost = 1,
                effects = new object[]
                {
                    new { action = "attack", scope = "single", side = "enemy", amount = 6 },
                },
            },
            upgraded = false,
        };
        var resp = await _client.PostAsJsonAsync("/api/dev/cards/preview", body);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var desc = json.GetProperty("description").GetString();
        Assert.NotNull(desc);
        Assert.Contains("[N:6]", desc);
    }

    [Fact]
    public async Task Preview_uses_upgraded_effects_when_upgraded_true()
    {
        var body = new
        {
            spec = new
            {
                rarity = 1,
                cardType = "Attack",
                cost = 1,
                effects = new object[]
                {
                    new { action = "attack", scope = "single", side = "enemy", amount = 6 },
                },
                upgradedEffects = new object[]
                {
                    new { action = "attack", scope = "single", side = "enemy", amount = 9 },
                },
            },
            upgraded = true,
        };
        var resp = await _client.PostAsJsonAsync("/api/dev/cards/preview", body);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var desc = json.GetProperty("description").GetString();
        Assert.NotNull(desc);
        Assert.Contains("[N:9]", desc);
    }

    [Fact]
    public async Task Preview_returns_400_for_invalid_spec()
    {
        // cardType missing → CardJsonException
        var body = new
        {
            spec = new { rarity = 1 },
            upgraded = false,
        };
        var resp = await _client.PostAsJsonAsync("/api/dev/cards/preview", body);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_override_only_does_not_remove_base_file()
    {
        // 1) override を直接 file 配置 (SaveVersion 経由は manifest を見るため、disk-only base には使えない)
        var overrideDir = Path.Combine(_overrideRoot, "cards");
        Directory.CreateDirectory(overrideDir);
        var overridePath = Path.Combine(overrideDir, "deletable_card.json");
        File.WriteAllText(overridePath,
            """{ "id": "deletable_card", "name": "削除対象", "activeVersion": "v1", "versions": [ { "version": "v1", "spec": { "rarity": 1, "cardType": "Skill", "cost": 2, "effects": [] } } ] }""");

        // 2) DELETE without alsoBase
        var resp = await _client.DeleteAsync("/api/dev/cards/deletable_card");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        // override file は消える
        Assert.False(File.Exists(overridePath));
        // base file は残る
        Assert.True(File.Exists(Path.Combine(_baseDir, "deletable_card.json")));
    }

    [Fact]
    public async Task Delete_with_alsoBase_removes_base_file_and_creates_backup()
    {
        // alsoBase=true で base file 削除 + backup
        var resp = await _client.DeleteAsync("/api/dev/cards/deletable_card?alsoBase=true");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var basePath = Path.Combine(_baseDir, "deletable_card.json");
        Assert.False(File.Exists(basePath));

        // backup が cards/ に取られている
        var backupCardsDir = Path.Combine(_backupRoot, "cards");
        Assert.True(Directory.Exists(backupCardsDir));
        var backups = Directory.GetFiles(backupCardsDir, "deletable_card-deleted-*.json");
        Assert.NotEmpty(backups);
    }

    [Fact]
    public async Task Delete_unknown_id_returns_404()
    {
        var resp = await _client.DeleteAsync("/api/dev/cards/nonexistent_xyz");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}

/// <summary>
/// Production 環境では preview / delete も 404 を返すこと。
/// </summary>
public class DevCardsPreviewProdTests : IClassFixture<ProductionWebApplicationFactory>
{
    private readonly ProductionWebApplicationFactory _factory;

    public DevCardsPreviewProdTests(ProductionWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Preview_returns_404_in_production()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/dev/cards/preview",
            new { spec = new { rarity = 1, cardType = "Skill", cost = 1, effects = Array.Empty<object>() }, upgraded = false });
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_returns_404_in_production()
    {
        var client = _factory.CreateClient();
        var resp = await client.DeleteAsync("/api/dev/cards/strike");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
