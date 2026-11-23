using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Core.Data;
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

        // The rebuilt universal basic defense is shared by every character.
        Assert.True(body.TryGetProperty("basic_defense", out var defend));
        Assert.Equal("basic_defense", defend.GetProperty("id").GetString());
        Assert.Equal("防御の基本", defend.GetProperty("name").GetString());
        Assert.Equal("Skill", defend.GetProperty("cardType").GetString());
        Assert.Equal(0, defend.GetProperty("usesPerTurn").GetInt32());
        Assert.Equal(JsonValueKind.Null, defend.GetProperty("upgradedUsesPerTurn").ValueKind);
        Assert.Equal(JsonValueKind.Array, defend.GetProperty("poolKinds").ValueKind);
        Assert.Equal(JsonValueKind.Array, defend.GetProperty("eligibleCharacterIds").ValueKind);
        Assert.True(defend.TryGetProperty("poolClass", out _));
    }

    [Fact]
    public async Task GetCards_Description_uses_CardTextFormatter()
    {
        // 10.5.B: 数字は [N:N] marker syntax で出力される。
        // 攻撃の基本 (single enemy attack 6) → 「敵単体に [N:6] アタック。」
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/v1/catalog/cards");
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("basic_attack", out var strike));
        // 攻撃の基本は使用者の攻撃力を参照する（戦闘外は使用者不明のため変数表示）。
        Assert.Equal("敵単体に[V:X|自身の攻撃力]アタック。", strike.GetProperty("description").GetString());
        // upgraded: 攻撃力の 1.5 倍
        Assert.Equal("敵単体に[V:X|自身の攻撃力×1.5]アタック。", strike.GetProperty("upgradedDescription").GetString());
    }

    [Fact]
    public async Task GetCards_Defend_description_is_block_via_formatter()
    {
        // 防御の基本は全クラス共通の自己防御カード。
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/v1/catalog/cards");
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("basic_defense", out var defend));
        Assert.Equal("自身に[V:X|自身の守備力]ブロック。", defend.GetProperty("description").GetString());
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
        // 敵はカード制御: 基礎能力値と行動デッキを公開する。
        Assert.True(jw.GetProperty("attack").GetInt32() > 0);
        Assert.True(jw.GetProperty("deck").GetArrayLength() > 0);
        Assert.Equal(JsonValueKind.Array, jw.GetProperty("openingActions").ValueKind);
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
        var runtimeCatalog = _factory.Services.GetRequiredService<DataCatalog>();
        Assert.Equal(3, wisp.GetProperty("heightTier").GetInt32());
        Assert.Equal(runtimeCatalog.Units!["wisp"].InitialEnergy,
            wisp.GetProperty("initialEnergy").GetInt32());
        Assert.Equal(runtimeCatalog.Units["wisp"].EnergyPerTurn,
            wisp.GetProperty("energyPerTurn").GetInt32());
        Assert.Equal(runtimeCatalog.Units["wisp"].DrawPerTurn,
            wisp.GetProperty("drawPerTurn").GetInt32());
        Assert.Equal(runtimeCatalog.Units["wisp"].HandLimit,
            wisp.GetProperty("handLimit").GetInt32());
        Assert.Equal("low_cost_combo", wisp.GetProperty("aiProfileId").GetString());
        Assert.Equal(JsonValueKind.Null, wisp.GetProperty("traitDescription").ValueKind);
        Assert.Empty(wisp.GetProperty("skills").EnumerateArray());
        Assert.Empty(wisp.GetProperty("ownedRelicIds").EnumerateArray());
        Assert.Empty(wisp.GetProperty("potions").EnumerateArray());
        var deck = wisp.GetProperty("deck");
        Assert.Equal(4, deck.GetArrayLength());
        Assert.Contains(deck.EnumerateArray(), entry =>
            entry.GetProperty("cardId").GetString() == "wisp_will_o_wisp"
            && entry.GetProperty("count").GetInt32() == 2);
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
        Assert.Equal("Striker", def.GetProperty("characterClass").GetString());
        Assert.Equal(3, def.GetProperty("energyPerTurn").GetInt32());
        Assert.Equal(5, def.GetProperty("drawPerTurn").GetInt32());
        Assert.Equal("連閃", def.GetProperty("traitName").GetString());
        Assert.NotEmpty(def.GetProperty("deck").EnumerateArray());
        var runtimeDefinition = _factory.Services.GetRequiredService<DataCatalog>().Characters["default"];
        Assert.Equal(runtimeDefinition.Description, def.GetProperty("description").GetString());
        Assert.Equal(runtimeDefinition.TraitDescription, def.GetProperty("traitDescription").GetString());
    }

    [Fact]
    public async Task GetCharacters_projects_skill_name_cost_limit_and_generated_effect_text()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/v1/catalog/characters");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var skills = doc.RootElement.GetProperty("default").GetProperty("skills");
        var tactical = skills.EnumerateArray().Single(s => s.GetProperty("id").GetString() == "tactical");

        Assert.Equal("刃風展開", tactical.GetProperty("name").GetString());
        Assert.Equal(3, tactical.GetProperty("comboGaugeCost").GetInt32());
        Assert.True(tactical.GetProperty("oncePerTurn").GetBoolean());
        Assert.Equal(1, tactical.GetProperty("usesPerTurn").GetInt32());
        var description = tactical.GetProperty("description").GetString();
        Assert.DoesNotContain("度だけ使用可能", description);
        Assert.Contains("[N:1]枚引く", description);
        Assert.Contains("エナジー+[N:1]", description);
        Assert.Contains("[S:omnistrike]", description);
    }

    [Fact]
    public async Task GetCharacters_projects_marker_formatted_trait_trigger_and_effect_text()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/v1/catalog/characters");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var striker = doc.RootElement.GetProperty("default").GetProperty("traitEffects").GetString();
        Assert.Contains("最大コンボが[N:4]以上", striker);
        Assert.Contains("[S:afterimage][N:1]", striker);
        Assert.Contains("1ターンに[N:1]度だけ発動", striker);

        var tactician = doc.RootElement.GetProperty("tactician_yuno").GetProperty("traitEffects").GetString();
        Assert.Contains("[K:contingency]が発動", tactician);
        Assert.Contains("エナジー+[N:1]", tactician);
        Assert.Contains("コンボゲージ+[N:1]", tactician);

        var enhancer = doc.RootElement.GetProperty("enhancer_lyra").GetProperty("traitEffects").GetString();
        Assert.Contains("[S:resonance]が発動", enhancer);
        Assert.Contains("味方全体に[N:3]ブロック", enhancer);
    }

    [Fact]
    public async Task GetPotions_projects_entity_level_usage_limit()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/v1/catalog/potions");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var block = doc.RootElement.GetProperty("block_potion");
        Assert.Equal(0, block.GetProperty("usesPerTurn").GetInt32());
    }
}
