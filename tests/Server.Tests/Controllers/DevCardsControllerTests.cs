using System;
using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

/// <summary>
/// Development 環境用の WebApplicationFactory。
/// /api/dev/cards endpoint は IsDevelopment() ガードのため、明示的に環境を切替えてテストする。
/// </summary>
public sealed class DevWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(), "rcg-dev-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string?>(
                    "DataStorage:RootDirectory", _dataRoot),
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && Directory.Exists(_dataRoot))
        {
            try { Directory.Delete(_dataRoot, recursive: true); } catch { }
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Production 環境用の WebApplicationFactory。dev endpoint が 404 になることを確認する。
/// </summary>
public sealed class ProductionWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(), "rcg-prod-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string?>(
                    "DataStorage:RootDirectory", _dataRoot),
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && Directory.Exists(_dataRoot))
        {
            try { Directory.Delete(_dataRoot, recursive: true); } catch { }
        }
        base.Dispose(disposing);
    }
}

public class DevCardsControllerTests : IClassFixture<DevWebApplicationFactory>
{
    private readonly DevWebApplicationFactory _factory;

    public DevCardsControllerTests(DevWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task GetCards_returns_200_in_dev_with_card_list()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/dev/cards");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.ValueKind);

        // 少なくとも strike と defend が含まれる。
        bool foundStrike = false;
        bool foundDefend = false;
        foreach (var entry in body.EnumerateArray())
        {
            var id = entry.GetProperty("id").GetString();
            if (id == "strike") foundStrike = true;
            if (id == "defend") foundDefend = true;

            // 各 entry が activeVersion / versions[] を持つこと。
            Assert.True(entry.TryGetProperty("activeVersion", out _));
            Assert.True(entry.TryGetProperty("versions", out var versions));
            Assert.Equal(JsonValueKind.Array, versions.ValueKind);
            Assert.True(versions.GetArrayLength() >= 1);
        }
        Assert.True(foundStrike, "strike not found in /api/dev/cards");
        Assert.True(foundDefend, "defend not found in /api/dev/cards");
    }

    [Fact]
    public async Task GetCards_strike_has_v1_active_with_spec()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/dev/cards");
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement? strike = null;
        foreach (var entry in body.EnumerateArray())
        {
            if (entry.GetProperty("id").GetString() == "strike")
            {
                strike = entry;
                break;
            }
        }
        Assert.NotNull(strike);

        Assert.Equal("v1", strike!.Value.GetProperty("activeVersion").GetString());
        var versions = strike.Value.GetProperty("versions");
        var v1 = versions[0];
        Assert.Equal("v1", v1.GetProperty("version").GetString());
        // spec は文字列 (raw JSON) として伝わる
        var spec = v1.GetProperty("spec").GetString();
        Assert.NotNull(spec);
        Assert.Contains("\"cardType\"", spec);
        Assert.Contains("\"effects\"", spec);
    }
}

public class DevCardsControllerProdTests : IClassFixture<ProductionWebApplicationFactory>
{
    private readonly ProductionWebApplicationFactory _factory;

    public DevCardsControllerProdTests(ProductionWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task GetCards_returns_404_in_production()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/dev/cards");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
