using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Controllers;

public sealed class DevContentControllerTests : IClassFixture<DevWebApplicationFactory>
{
    private readonly DevWebApplicationFactory _factory;

    public DevContentControllerTests(DevWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Kinds_exposes_every_supported_authoring_layer()
    {
        var response = await _factory.CreateClient().GetAsync("/api/dev/content/kinds");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var kinds = body.GetProperty("kinds").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString())
            .ToArray();

        Assert.Contains("cards", kinds);
        Assert.Contains("potions", kinds);
        Assert.Contains("relics", kinds);
        Assert.Contains("characters", kinds);
        Assert.Contains("enemies", kinds);
        Assert.Contains("units", kinds);
        Assert.Contains("encounters", kinds);
        Assert.Contains("events", kinds);
        Assert.Contains("rewardTables", kinds);
        Assert.Contains("actStartRelicPools", kinds);
        Assert.Contains("merchantPrices", kinds);
        Assert.Contains("mapConfigs", kinds);
        Assert.Contains("statusDefinitions", kinds);
        Assert.Contains("effectDefinitions", kinds);

        var labels = body.GetProperty("enums").GetProperty("optionLabels");
        Assert.Equal("バフ", labels.GetProperty("actions:buff").GetString());
        Assert.Equal("除外回収", labels.GetProperty("actions:recoverFromExhaust").GetString());
        Assert.Equal("筋力", labels.GetProperty("statuses:strength").GetString());
        Assert.Equal("残光", labels.GetProperty("statuses:afterimage").GetString());
        Assert.Equal("堅持", labels.GetProperty("statuses:steadfast").GetString());
        Assert.Equal("護衛", labels.GetProperty("statuses:cover").GetString());
        Assert.Equal("守勢", labels.GetProperty("statuses:defensiveStance").GetString());
        Assert.Equal("連環", labels.GetProperty("statuses:comboCarryover").GetString());
        Assert.Equal("余韻", labels.GetProperty("statuses:afterglow").GetString());
        Assert.Equal("共鳴", labels.GetProperty("statuses:resonance").GetString());
        Assert.Equal("攪乱", labels.GetProperty("statuses:disruption").GetString());
        Assert.Equal("対象カードの現在コスト", labels.GetProperty("amountSources:currentCost").GetString());
        Assert.Equal("休息所", labels.GetProperty("tileKinds:Rest").GetString());
        Assert.Equal("ストライカー", labels.GetProperty("characterClasses:Striker").GetString());
        Assert.Equal("ユニバーサル", labels.GetProperty("cardPoolClasses:Universal").GetString());
        Assert.Equal("報酬カード", labels.GetProperty("cardPoolKinds:Reward").GetString());
        Assert.Equal("終撃", labels.GetProperty("cardAbilities:finisher").GetString());
        Assert.Equal("相手のアタック時", labels.GetProperty("contingencyTriggers:BeforeEnemyAttack").GetString());
        Assert.Equal("最大コンボ達成時", labels.GetProperty("characterTraitTriggers:OnComboReached").GetString());
        Assert.Equal("共鳴発動後", labels.GetProperty("characterTraitTriggers:OnResonanceConsumed").GetString());

        var enums = body.GetProperty("enums");
        Assert.Contains(enums.GetProperty("characterClasses").EnumerateArray(), value => value.GetString() == "Guardian");
        Assert.Contains(enums.GetProperty("cardPoolClasses").EnumerateArray(), value => value.GetString() == "Universal");
        Assert.Contains(enums.GetProperty("cardPoolKinds").EnumerateArray(), value => value.GetString() == "Basic");
        Assert.Contains(enums.GetProperty("cardAbilities").EnumerateArray(), value => value.GetString() == "reprise");
        Assert.Contains(enums.GetProperty("contingencyTriggerTimings").EnumerateArray(), value => value.GetString() == "ComboReached");
        Assert.Contains(enums.GetProperty("characterTraitTriggers").EnumerateArray(), value => value.GetString() == "OnContingencyTriggered");
        Assert.Contains(enums.GetProperty("eventEffectTypes").EnumerateArray(), value => value.GetString() == "gainCard");
        Assert.Contains(enums.GetProperty("actions").EnumerateArray(), value => value.GetString() == "gainComboGauge");
        Assert.Contains(enums.GetProperty("amountSources").EnumerateArray(), value => value.GetString() == "currentCost");
    }

    [Theory]
    [InlineData("cards")]
    [InlineData("potions")]
    [InlineData("relics")]
    [InlineData("characters")]
    [InlineData("enemies")]
    [InlineData("units")]
    [InlineData("encounters")]
    [InlineData("events")]
    [InlineData("rewardTables")]
    [InlineData("actStartRelicPools")]
    [InlineData("merchantPrices")]
    [InlineData("mapConfigs")]
    public async Task Backed_kind_can_be_listed(string kind)
    {
        var response = await _factory.CreateClient().GetAsync($"/api/dev/content/{kind}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.True(body.GetArrayLength() > 0);
    }

    [Fact]
    public async Task Lists_expose_classification_needed_by_workbench_filters()
    {
        var client = _factory.CreateClient();
        var cards = await client.GetFromJsonAsync<JsonElement>("/api/dev/content/cards");
        var basicAttack = cards.EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "basic_attack");
        Assert.Equal("Universal", basicAttack.GetProperty("poolClass").GetString());

        var poolless = cards.EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "starter_summon_3");
        Assert.Equal(JsonValueKind.Null, poolless.GetProperty("poolClass").ValueKind);

        var characters = await client.GetFromJsonAsync<JsonElement>("/api/dev/content/characters");
        var defaultCharacter = characters.EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "default");
        Assert.Equal("Striker", defaultCharacter.GetProperty("characterClass").GetString());
    }

    [Fact]
    public async Task Validation_rejects_route_and_document_id_mismatch_without_writing()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/api/dev/content/potions/health_potion/validate",
            new { document = new { id = "different", name = "x", rarity = 1, effects = System.Array.Empty<object>() } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Event_preview_uses_the_loaded_event_rules()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/api/dev/content/preview",
            new
            {
                kind = "events",
                document = new
                {
                    id = "preview_event",
                    name = "確認イベント",
                    tiers = new[] { 1 },
                    rarity = "common",
                    startMessage = "道を選ぶ。",
                    choices = new[]
                    {
                        new
                        {
                            label = "進む",
                            effects = new[] { new { type = "gainGold", amount = 12 } },
                            resultMessage = "金貨を得た。",
                        },
                    },
                },
            });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var description = body.GetProperty("description").GetString();
        Assert.Contains("道を選ぶ。", description);
        Assert.Contains("ゴールド+12", description);
        Assert.Contains("金貨を得た。", description);
    }

    [Fact]
    public async Task Event_preview_describes_a_specific_gainCard_effect()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/api/dev/content/preview",
            new
            {
                kind = "events",
                document = new
                {
                    id = "card_event_preview",
                    name = "カードイベント",
                    tiers = new[] { 1 },
                    rarity = "common",
                    startMessage = "カードを選ぶ。",
                    choices = new[]
                    {
                        new
                        {
                            label = "受け取る",
                            effects = new[] { new { type = "gainCard", cardId = "strike" } },
                            resultMessage = "受け取った。",
                        },
                    },
                },
            });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("カード「strike」を獲得", body.GetProperty("description").GetString());
    }

    [Fact]
    public async Task Character_preview_exposes_class_and_initial_deck()
    {
        var document = await _factory.CreateClient()
            .GetFromJsonAsync<JsonElement>("/api/dev/content/characters/default");
        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/api/dev/content/preview",
            new { kind = "characters", document = document.GetProperty("document") });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("ストライカー", body.GetProperty("classification").GetString());
        Assert.NotEmpty(body.GetProperty("initialDeck").EnumerateArray());
        Assert.Equal(3, body.GetProperty("energyPerTurn").GetInt32());
        Assert.Equal(5, body.GetProperty("drawPerTurn").GetInt32());
        Assert.Equal("連閃", body.GetProperty("traitName").GetString());
        Assert.Equal("最大コンボ達成時", body.GetProperty("traitTrigger").GetString());
        Assert.Contains("[S:afterimage]", body.GetProperty("traitEffects").GetString());
    }

    [Fact]
    public async Task Card_preview_explains_pool_and_empty_character_restriction()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/api/dev/content/preview",
            new
            {
                kind = "cards",
                document = new
                {
                    id = "pool_preview",
                    name = "分類確認",
                    poolKinds = new[] { "Reward" },
                    poolClass = "Striker",
                    eligibleCharacterIds = System.Array.Empty<string>(),
                    activeVersion = "v1",
                    versions = new[]
                    {
                        new
                        {
                            version = "v1",
                            spec = new
                            {
                                rarity = 1,
                                cardType = "Skill",
                                cost = 1,
                                effects = System.Array.Empty<object>(),
                            },
                        },
                    },
                },
            });

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var classification = body.GetProperty("classification").GetString();
        Assert.Contains("報酬カード", classification);
        Assert.Contains("ストライカー", classification);
        Assert.Contains("同クラスの全キャラクター", classification);
    }

    [Fact]
    public async Task E2e_can_inspect_but_cannot_write_repository_overrides()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment("E2E"));
        using var client = factory.CreateClient();

        var metaResponse = await client.GetAsync("/api/dev/content/kinds");
        metaResponse.EnsureSuccessStatusCode();
        var meta = await metaResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(meta.GetProperty("readOnly").GetBoolean());

        var writeResponse = await client.PutAsJsonAsync(
            "/api/dev/content/cards/should_not_write",
            new
            {
                document = new
                {
                    id = "should_not_write",
                    name = "No",
                    activeVersion = "v1",
                    versions = new[] { new { version = "v1", spec = new { rarity = 1, cardType = "Skill", cost = 1, effects = System.Array.Empty<object>() } } },
                },
            });
        Assert.Equal(HttpStatusCode.NotFound, writeResponse.StatusCode);
    }
}
