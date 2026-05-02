using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using RoguelikeCardGame.Server.Dtos;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public class CatalogControllerTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;

    public CatalogControllerTests(TempDataFactory factory) => _factory = factory;

    [Fact]
    public async Task GetCards_Returns200_WithDictionaryOfDefinitions()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/v1/catalog/cards");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Object, body.ValueKind);

        // "defend" exists in the embedded catalog and is named 「防御」.
        Assert.True(body.TryGetProperty("defend", out var defend));
        Assert.Equal("defend", defend.GetProperty("id").GetString());
        Assert.Equal("防御", defend.GetProperty("name").GetString());
        Assert.Equal("Skill", defend.GetProperty("cardType").GetString());
    }

    [Fact]
    public async Task GetCards_Description_uses_CardTextFormatter()
    {
        // 10.5.B: 数字は [N:N] marker syntax で出力される。
        // strike (single enemy attack 6) → 「敵単体に [N:6] アタック。」
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/v1/catalog/cards");
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("strike", out var strike));
        Assert.Equal("敵単体に[N:6]アタック。", strike.GetProperty("description").GetString());
        // upgraded: 9 アタック
        Assert.Equal("敵単体に[N:9]アタック。", strike.GetProperty("upgradedDescription").GetString());
    }

    [Fact]
    public async Task GetCards_Defend_description_is_block_via_formatter()
    {
        // defend は scope=Single, side=Ally のため、formatter は「味方単体に [N:5] ブロック。」を返す。
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/v1/catalog/cards");
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("defend", out var defend));
        Assert.Equal("味方単体に[N:5]ブロック。", defend.GetProperty("description").GetString());
    }

    [Fact]
    public async Task GetRelics_Returns200WithAll4Relics()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/v1/catalog/relics");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var list = await resp.Content.ReadFromJsonAsync<List<RelicDto>>();
        Assert.NotNull(list);
        var ids = list!.Select(r => r.Id).ToHashSet();
        Assert.Contains("extra_max_hp", ids);
        Assert.Contains("coin_purse", ids);
        Assert.Contains("traveler_boots", ids);
        Assert.Contains("warm_blanket", ids);
    }

    [Fact]
    public async Task GetEvents_Returns200WithAll3Events()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/v1/catalog/events");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var list = await resp.Content.ReadFromJsonAsync<List<EventDto>>();
        Assert.NotNull(list);
        var ids = list!.Select(e => e.Id).ToHashSet();
        Assert.Contains("blessing_fountain", ids);
        Assert.Contains("shady_merchant", ids);
        Assert.Contains("old_library", ids);
    }

    [Fact]
    public async Task GetEnemies_Returns200_WithDictionaryOfDefinitions()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/v1/catalog/enemies");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Object, body.ValueKind);

        // jaw_worm is an embedded enemy; verify shape.
        Assert.True(body.TryGetProperty("jaw_worm", out var jw));
        Assert.Equal("jaw_worm", jw.GetProperty("id").GetString());
        Assert.False(string.IsNullOrEmpty(jw.GetProperty("name").GetString()));
        Assert.False(string.IsNullOrEmpty(jw.GetProperty("imageId").GetString()));
        Assert.True(jw.GetProperty("hp").GetInt32() > 0);
        Assert.False(string.IsNullOrEmpty(jw.GetProperty("initialMoveId").GetString()));
    }

    [Fact]
    public async Task GetUnits_Returns200_WithDictionary()
    {
        // Units catalog はまだ embed されていないため空辞書だが、endpoint は 200 を返す。
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/v1/catalog/units");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Object, body.ValueKind);
    }

    [Fact]
    public async Task GetEnemies_returns_heightTier()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/v1/catalog/enemies");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        // dire_wolf は spec で tier 6
        var wolf = doc.RootElement.GetProperty("dire_wolf");
        Assert.Equal(6, wolf.GetProperty("heightTier").GetInt32());
    }

    [Fact]
    public async Task GetUnits_returns_heightTier()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/v1/catalog/units");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var wisp = doc.RootElement.GetProperty("wisp");
        Assert.Equal(3, wisp.GetProperty("heightTier").GetInt32());
    }

    [Fact]
    public async Task GetCharacters_returns_default_with_heightTier()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/v1/catalog/characters");
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var def = doc.RootElement.GetProperty("default");
        Assert.Equal("default", def.GetProperty("id").GetString());
        Assert.Equal(5, def.GetProperty("heightTier").GetInt32());
    }
}
