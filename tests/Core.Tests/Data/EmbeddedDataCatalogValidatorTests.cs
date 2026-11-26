using System;
using RoguelikeCardGame.Core.Data;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Data;

public sealed class EmbeddedDataCatalogValidatorTests
{
    [Fact]
    public void EmbeddedCatalog_HasNoImplementedRelicWithoutEffects()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();

        Assert.DoesNotContain(catalog.Relics.Values,
            relic => relic.Implemented && relic.Effects.Count == 0);
    }

    [Fact]
    public void Validator_RejectsImplementedRelicWithoutEffects()
    {
        const string json = """
            {"id":"empty","name":"Empty","rarity":0,"effects":[],"implemented":true}
            """;

        var error = Assert.Throws<DataCatalogException>(() =>
            EmbeddedDataCatalogValidator.ValidateRelics(new[] { json }));

        Assert.Contains("empty", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "{\"action\":\"notImplemented\",\"scope\":\"self\",\"amount\":1,\"trigger\":\"OnBattleStart\"}",
        "notImplemented")]
    [InlineData(
        "{\"action\":\"buff\",\"scope\":\"self\",\"amount\":1,\"name\":\"strength\",\"trigger\":\"OnBattleEnd\"}",
        "OnBattleEnd")]
    [InlineData(
        "{\"action\":\"gainGold\",\"scope\":\"self\",\"amount\":1,\"trigger\":\"OnBattleStart\"}",
        "gainGold")]
    [InlineData(
        "{\"action\":\"buff\",\"scope\":\"self\",\"amount\":1,\"name\":\"strength\"}",
        "trigger")]
    public void Validator_RejectsEffectsThatCoreCannotDispatch(
        string effectJson, string expectedMessage)
    {
        var error = Assert.Throws<DataCatalogException>(() =>
            EmbeddedDataCatalogValidator.ValidateRelics(new[] { RelicWith(effectJson) }));

        Assert.Contains("invalid", error.Message, StringComparison.Ordinal);
        Assert.Contains(expectedMessage, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "{\"action\":\"block\",\"scope\":\"all\",\"amount\":2,\"trigger\":\"OnBattleStart\"}",
        "side")]
    [InlineData(
        "{\"action\":\"attack\",\"scope\":\"self\",\"side\":\"enemy\",\"amount\":2,\"trigger\":\"OnHit\"}",
        "scope")]
    [InlineData(
        "{\"action\":\"gainGold\",\"scope\":\"all\",\"side\":\"ally\",\"amount\":2,\"trigger\":\"OnPickup\"}",
        "scope 'self'")]
    [InlineData(
        "{\"action\":\"draw\",\"scope\":\"all\",\"side\":\"enemy\",\"amount\":1,\"trigger\":\"OnTurnStart\"}",
        "side 'ally'")]
    public void Validator_RejectsInvalidScopeAndSideCombinations(
        string effectJson, string expectedMessage)
    {
        var error = Assert.Throws<DataCatalogException>(() =>
            EmbeddedDataCatalogValidator.ValidateRelics(new[] { RelicWith(effectJson) }));

        Assert.Contains(expectedMessage, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("draw", "all", "ally")]
    [InlineData("gainEnergy", "single", "ally")]
    [InlineData("gainMaxEnergy", "random", "ally")]
    [InlineData("modifyCardCost", "all", "ally")]
    public void Validator_AcceptsParticipantResourceEffectsTargetingAllies(
        string action, string scope, string side)
    {
        string extra = action == "modifyCardCost"
            ? ",\"pile\":\"hand\",\"select\":\"all\",\"costDuration\":\"untilPlayed\""
            : "";
        string effect =
            $"{{\"action\":\"{action}\",\"scope\":\"{scope}\",\"side\":\"{side}\",\"amount\":1,\"trigger\":\"OnTurnStart\"{extra}}}";

        EmbeddedDataCatalogValidator.ValidateRelics(new[] { RelicWith(effect) });
    }

    [Theory]
    [InlineData("draw")]
    [InlineData("gainEnergy")]
    [InlineData("gainMaxEnergy")]
    [InlineData("modifyCardCost")]
    public void Validator_RejectsParticipantResourceEffectsTargetingEnemies(string action)
    {
        string extra = action == "modifyCardCost"
            ? ",\"pile\":\"hand\",\"select\":\"all\",\"costDuration\":\"untilPlayed\""
            : "";
        string effect =
            $"{{\"action\":\"{action}\",\"scope\":\"single\",\"side\":\"enemy\",\"amount\":1,\"trigger\":\"OnTurnStart\"{extra}}}";

        var error = Assert.Throws<DataCatalogException>(() =>
            EmbeddedDataCatalogValidator.ValidateRelics(new[] { RelicWith(effect) }));

        Assert.Contains("side 'ally'", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(
        "{\"action\":\"buff\",\"scope\":\"self\",\"amount\":1,\"name\":\"not_a_status\",\"trigger\":\"OnBattleStart\"}",
        "does not exist")]
    [InlineData(
        "{\"action\":\"buff\",\"scope\":\"self\",\"amount\":1,\"name\":\"weak\",\"trigger\":\"OnBattleStart\"}",
        "cannot apply")]
    [InlineData(
        "{\"action\":\"debuff\",\"scope\":\"single\",\"side\":\"enemy\",\"amount\":1,\"trigger\":\"OnHit\"}",
        "status ID")]
    public void Validator_RejectsInvalidStatusReferences(
        string effectJson, string expectedMessage)
    {
        var error = Assert.Throws<DataCatalogException>(() =>
            EmbeddedDataCatalogValidator.ValidateRelics(new[] { RelicWith(effectJson) }));

        Assert.Contains(expectedMessage, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_Rejects_multi_stack_virulent_poison_application()
    {
        const string effect =
            "{\"action\":\"debuff\",\"scope\":\"single\",\"side\":\"enemy\",\"amount\":2,\"name\":\"virulentPoison\",\"trigger\":\"OnHit\"}";

        var error = Assert.Throws<DataCatalogException>(() =>
            EmbeddedDataCatalogValidator.ValidateRelics(new[] { RelicWith(effect) }));

        Assert.Contains("one stack at a time", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Validator_RejectsChanceOutsidePercentRange(int chancePercent)
    {
        var effect = $$"""
            {"action":"debuff","scope":"single","side":"enemy","amount":1,"name":"weak","trigger":"OnHit","chancePercent":{{chancePercent}}}
            """;

        var error = Assert.Throws<DataCatalogException>(() =>
            EmbeddedDataCatalogValidator.ValidateRelics(new[] { RelicWith(effect) }));

        Assert.Contains("chancePercent", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_RejectsChanceOnTriggerThatDoesNotRollIt()
    {
        const string effect =
            "{\"action\":\"buff\",\"scope\":\"self\",\"amount\":1,\"name\":\"strength\",\"trigger\":\"OnTurnStart\",\"chancePercent\":50}";

        var error = Assert.Throws<DataCatalogException>(() =>
            EmbeddedDataCatalogValidator.ValidateRelics(new[] { RelicWith(effect) }));

        Assert.Contains("OnHit", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "{\"action\":\"summon\",\"scope\":\"self\",\"amount\":0,\"trigger\":\"OnBattleStart\"}",
        "unitId")]
    [InlineData(
        "{\"action\":\"addCard\",\"scope\":\"self\",\"amount\":1,\"pile\":\"hand\",\"trigger\":\"OnBattleStart\"}",
        "cardRefId")]
    [InlineData(
        "{\"action\":\"upgrade\",\"scope\":\"self\",\"amount\":1,\"trigger\":\"OnBattleStart\"}",
        "pile")]
    [InlineData(
        "{\"action\":\"attack\",\"scope\":\"single\",\"side\":\"enemy\",\"amount\":1,\"amountSource\":\"typo\",\"trigger\":\"OnHit\"}",
        "amountSource")]
    [InlineData(
        "{\"action\":\"attack\",\"scope\":\"single\",\"side\":\"enemy\",\"amount\":1,\"amountSource\":\"currentCost\",\"trigger\":\"OnHit\"}",
        "only")]
    [InlineData(
        "{\"action\":\"unknownTileWeightDelta\",\"scope\":\"self\",\"amount\":1,\"name\":\"boss\",\"trigger\":\"Passive\"}",
        "unknownTileWeightDelta")]
    public void Validator_RejectsMissingOrUnsupportedActionParameters(
        string effectJson, string expectedMessage)
    {
        var error = Assert.Throws<DataCatalogException>(() =>
            EmbeddedDataCatalogValidator.ValidateRelics(new[] { RelicWith(effectJson) }));

        Assert.Contains(expectedMessage, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("{\"action\":\"setPhase\",\"scope\":\"self\",\"amount\":0,\"name\":\"calm\",\"trigger\":\"OnBattleStart\"}")]
    [InlineData("{\"action\":\"invertPhase\",\"scope\":\"self\",\"amount\":0,\"trigger\":\"OnTurnStart\"}")]
    [InlineData("{\"action\":\"augmentCards\",\"scope\":\"self\",\"amount\":1,\"name\":\"bipolar\",\"pile\":\"hand\",\"select\":\"all\",\"trigger\":\"OnTurnStart\"}")]
    public void Validator_AcceptsPhaseAndAugmentRelicEffects(string effectJson) =>
        EmbeddedDataCatalogValidator.ValidateRelics(new[] { RelicWith(effectJson) });

    [Fact]
    public void Validator_RejectsTriggeredChooseAugment()
    {
        var json = "{\"action\":\"augmentCards\",\"scope\":\"self\",\"amount\":1,\"name\":\"bipolar\",\"pile\":\"hand\",\"select\":\"choose\",\"trigger\":\"OnTurnStart\"}";

        var error = Assert.Throws<DataCatalogException>(() =>
            EmbeddedDataCatalogValidator.ValidateRelics(new[] { RelicWith(json) }));

        Assert.Contains("choose", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validator_SkipsPlaceholderRelicSemantics()
    {
        const string json = """
            {"id":"placeholder","name":"Placeholder","rarity":0,
             "effects":[{"action":"futureAction","scope":"self","amount":0}],
             "implemented":false}
            """;

        EmbeddedDataCatalogValidator.ValidateRelics(new[] { json });
    }

    [Fact]
    public void Validator_RejectsTriggeredChooseCardCostModifier()
    {
        const string effect =
            "{\"action\":\"modifyCardCost\",\"scope\":\"self\",\"amount\":-1,\"pile\":\"hand\",\"select\":\"choose\",\"cardCount\":1,\"costDuration\":\"untilPlayed\",\"trigger\":\"OnBattleStart\"}";

        var error = Assert.Throws<DataCatalogException>(() =>
            EmbeddedDataCatalogValidator.ValidateRelics(new[] { RelicWith(effect) }));

        Assert.Contains("cannot use select choose", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_AcceptsCurrentCostForCardCostModifier()
    {
        const string effect =
            "{\"action\":\"modifyCardCost\",\"scope\":\"self\",\"amount\":0,\"amountSource\":\"currentCost\",\"pile\":\"hand\",\"select\":\"all\",\"costDuration\":\"untilPlayed\",\"trigger\":\"OnBattleStart\"}";

        EmbeddedDataCatalogValidator.ValidateRelics(new[] { RelicWith(effect) });
    }

    [Fact]
    public void GenericCatalogLoader_StillAllowsPurposeBuiltEmptyRelic()
    {
        const string relicJson = """
            {"id":"empty","name":"Empty","rarity":0,"effects":[],"implemented":true}
            """;

        var catalog = DataCatalog.LoadFromStrings(
            cards: Array.Empty<string>(),
            relics: new[] { relicJson },
            potions: Array.Empty<string>(),
            enemies: Array.Empty<string>(),
            encounters: Array.Empty<string>(),
            rewardTables: Array.Empty<string>(),
            characters: Array.Empty<string>());

        Assert.Empty(catalog.Relics["empty"].Effects);
    }

    [Theory]
    [InlineData("coin_purse", "gainGold", "OnPickup", 50)]
    [InlineData("extra_max_hp", "gainMaxHp", "OnPickup", 7)]
    [InlineData("traveler_boots", "gainGold", "OnMapTileResolved", 1)]
    [InlineData("warm_blanket", "restHealBonus", "Passive", 10)]
    [InlineData("act2_start_01", "gainMaxHp", "OnPickup", 12)]
    [InlineData("act2_start_02", "gainGold", "OnPickup", 100)]
    [InlineData("act2_start_03", "restHealBonus", "Passive", 8)]
    [InlineData("act2_start_04", "gainMaxHp", "OnPickup", 10)]
    [InlineData("act2_start_05", "gainGold", "OnPickup", 80)]
    [InlineData("act3_start_01", "gainMaxHp", "OnPickup", 20)]
    [InlineData("act3_start_02", "gainGold", "OnPickup", 150)]
    [InlineData("act3_start_03", "restHealBonus", "Passive", 12)]
    [InlineData("act3_start_04", "gainMaxHp", "OnPickup", 15)]
    [InlineData("act3_start_05", "gainGold", "OnPickup", 120)]
    public void RestoredRelic_HasCanonicalEffect(
        string id, string action, string trigger, int amount)
    {
        var relic = EmbeddedDataLoader.LoadCatalog().Relics[id];

        var effect = Assert.Single(relic.Effects);
        Assert.Equal(action, effect.Action);
        Assert.Equal(trigger, effect.Trigger);
        Assert.Equal(amount, effect.Amount);
    }

    private static string RelicWith(string effectJson) => $$"""
        {"id":"invalid","name":"Invalid","rarity":0,"effects":[{{effectJson}}],"implemented":true}
        """;
}
