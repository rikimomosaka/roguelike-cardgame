using System.Collections.Generic;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Core.Tests;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Relics;

public class PassiveModifiersTests
{
    private static readonly DataCatalog BaseCatalog = EmbeddedDataLoader.LoadCatalog();

    private static RunState Sample(int gold = 100, IReadOnlyList<string>? relics = null) =>
        RunState.NewSoloRun(
            BaseCatalog, 1UL, 0,
            ImmutableDictionary<int, TileKind>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            new System.DateTimeOffset(2026, 5, 4, 0, 0, 0, System.TimeSpan.Zero)
        ) with { Gold = gold, Relics = relics ?? new List<string>() };

    private static DataCatalog Cat(string id, CardEffect[] effects, bool implemented = true) =>
        RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(BaseCatalog, id, effects, implemented);

    [Fact]
    public void ApplyEnergyPerTurnBonus_NoRelics_ReturnsBase()
    {
        var s = Sample();
        Assert.Equal(3, PassiveModifiers.ApplyEnergyPerTurnBonus(3, s, BaseCatalog));
    }

    [Fact]
    public void ApplyEnergyPerTurnBonus_OnePassiveRelic_AddsAmount()
    {
        var fake = Cat("e1", new[] { new CardEffect("energyPerTurnBonus", EffectScope.Self, null, 1, Trigger: "Passive") });
        var s = Sample(relics: new List<string> { "e1" });
        Assert.Equal(4, PassiveModifiers.ApplyEnergyPerTurnBonus(3, s, fake));
    }

    [Fact]
    public void ApplyEnergyPerTurnBonus_TwoRelics_SumsAmounts()
    {
        var fake = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(BaseCatalog,
            "e1", new[] { new CardEffect("energyPerTurnBonus", EffectScope.Self, null, 1, Trigger: "Passive") });
        fake = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(fake,
            "e2", new[] { new CardEffect("energyPerTurnBonus", EffectScope.Self, null, 2, Trigger: "Passive") });
        var s = Sample(relics: new List<string> { "e1", "e2" });
        Assert.Equal(6, PassiveModifiers.ApplyEnergyPerTurnBonus(3, s, fake));
    }

    [Fact]
    public void ApplyEnergyPerTurnBonus_NotImplementedRelic_NoOp()
    {
        var fake = Cat("e1", new[] { new CardEffect("energyPerTurnBonus", EffectScope.Self, null, 5, Trigger: "Passive") }, implemented: false);
        var s = Sample(relics: new List<string> { "e1" });
        Assert.Equal(3, PassiveModifiers.ApplyEnergyPerTurnBonus(3, s, fake));
    }

    [Fact]
    public void ApplyEnergyPerTurnBonus_NonPassiveTrigger_Ignored()
    {
        var fake = Cat("e1", new[] { new CardEffect("energyPerTurnBonus", EffectScope.Self, null, 5, Trigger: "OnPickup") });
        var s = Sample(relics: new List<string> { "e1" });
        Assert.Equal(3, PassiveModifiers.ApplyEnergyPerTurnBonus(3, s, fake));
    }

    [Fact]
    public void ApplyEnergyPerTurnBonus_FloorAtZero()
    {
        var fake = Cat("e1", new[] { new CardEffect("energyPerTurnBonus", EffectScope.Self, null, -10, Trigger: "Passive") });
        var s = Sample(relics: new List<string> { "e1" });
        Assert.Equal(0, PassiveModifiers.ApplyEnergyPerTurnBonus(3, s, fake));
    }

    [Fact]
    public void ApplyCardsDrawnPerTurnBonus_AddsAmount()
    {
        var fake = Cat("d1", new[] { new CardEffect("cardsDrawnPerTurnBonus", EffectScope.Self, null, 2, Trigger: "Passive") });
        var s = Sample(relics: new List<string> { "d1" });
        Assert.Equal(7, PassiveModifiers.ApplyCardsDrawnPerTurnBonus(5, s, fake));
    }

    [Fact]
    public void ApplyCardsDrawnPerTurnBonus_FloorAtZero()
    {
        var fake = Cat("d1", new[] { new CardEffect("cardsDrawnPerTurnBonus", EffectScope.Self, null, -10, Trigger: "Passive") });
        var s = Sample(relics: new List<string> { "d1" });
        Assert.Equal(0, PassiveModifiers.ApplyCardsDrawnPerTurnBonus(5, s, fake));
    }

    [Fact]
    public void ApplyGoldRewardMultiplier_PositiveDelta_Increases()
    {
        var fake = Cat("g1", new[] { new CardEffect("goldRewardMultiplier", EffectScope.Self, null, 50, Trigger: "Passive") });
        var s = Sample(relics: new List<string> { "g1" });
        Assert.Equal(150, PassiveModifiers.ApplyGoldRewardMultiplier(100, s, fake));
    }

    [Fact]
    public void ApplyGoldRewardMultiplier_NegativeDelta_FloorAtZero()
    {
        var fake = Cat("g1", new[] { new CardEffect("goldRewardMultiplier", EffectScope.Self, null, -200, Trigger: "Passive") });
        var s = Sample(relics: new List<string> { "g1" });
        Assert.Equal(0, PassiveModifiers.ApplyGoldRewardMultiplier(100, s, fake));
    }

    [Fact]
    public void ApplyShopPriceMultiplier_NegativeDelta_FloorAtOne()
    {
        var fake = Cat("s1", new[] { new CardEffect("shopPriceMultiplier", EffectScope.Self, null, -200, Trigger: "Passive") });
        var s = Sample(relics: new List<string> { "s1" });
        Assert.Equal(1, PassiveModifiers.ApplyShopPriceMultiplier(50, s, fake));
    }

    [Fact]
    public void ApplyRewardCardChoicesBonus_AddsAmount()
    {
        var fake = Cat("r1", new[] { new CardEffect("rewardCardChoicesBonus", EffectScope.Self, null, 1, Trigger: "Passive") });
        var s = Sample(relics: new List<string> { "r1" });
        Assert.Equal(4, PassiveModifiers.ApplyRewardCardChoicesBonus(3, s, fake));
    }

    [Fact]
    public void ApplyRewardCardChoicesBonus_FloorAtOne()
    {
        var fake = Cat("r1", new[] { new CardEffect("rewardCardChoicesBonus", EffectScope.Self, null, -10, Trigger: "Passive") });
        var s = Sample(relics: new List<string> { "r1" });
        Assert.Equal(1, PassiveModifiers.ApplyRewardCardChoicesBonus(3, s, fake));
    }

    [Fact]
    public void HasPassiveCapability_PresentWithPositiveAmount_ReturnsTrue()
    {
        var fake = Cat("c1", new[] { new CardEffect("rewardRerollAvailable", EffectScope.Self, null, 1, Trigger: "Passive") });
        var s = Sample(relics: new List<string> { "c1" });
        Assert.True(PassiveModifiers.HasPassiveCapability("rewardRerollAvailable", s, fake));
    }

    [Fact]
    public void HasPassiveCapability_NotPresent_ReturnsFalse()
    {
        var s = Sample();
        Assert.False(PassiveModifiers.HasPassiveCapability("rewardRerollAvailable", s, BaseCatalog));
    }

    [Fact]
    public void ApplyUnknownWeightDeltas_AddsToBaseWeights()
    {
        var fake = Cat("u1", new[] {
            new CardEffect("unknownEnemyWeightDelta", EffectScope.Self, null, -3, Trigger: "Passive"),
            new CardEffect("unknownTreasureWeightDelta", EffectScope.Self, null, 5, Trigger: "Passive"),
        });
        var s = Sample(relics: new List<string> { "u1" });
        var config = new UnknownResolutionConfig(
            ImmutableDictionary.CreateRange(new[] {
                new System.Collections.Generic.KeyValuePair<TileKind, double>(TileKind.Enemy, 10.0),
                new System.Collections.Generic.KeyValuePair<TileKind, double>(TileKind.Treasure, 2.0),
            }));
        var weights = PassiveModifiers.ApplyUnknownWeightDeltas(config, s, fake);
        Assert.Equal(7.0, weights[TileKind.Enemy]);
        Assert.Equal(7.0, weights[TileKind.Treasure]);
    }

    [Fact]
    public void ApplyUnknownWeightDeltas_NegativeWouldGoBelowZero_FloorAtZero()
    {
        var fake = Cat("u1", new[] {
            new CardEffect("unknownEnemyWeightDelta", EffectScope.Self, null, -100, Trigger: "Passive"),
        });
        var s = Sample(relics: new List<string> { "u1" });
        var config = new UnknownResolutionConfig(
            ImmutableDictionary.CreateRange(new[] {
                new System.Collections.Generic.KeyValuePair<TileKind, double>(TileKind.Enemy, 10.0),
            }));
        var weights = PassiveModifiers.ApplyUnknownWeightDeltas(config, s, fake);
        Assert.Equal(0.0, weights[TileKind.Enemy]);
    }

    [Fact]
    public void ApplyPassiveRestHealBonus_RestHealBonus_AddsAmount()
    {
        var fake = Cat("h1", new[] { new CardEffect("restHealBonus", EffectScope.Self, null, 5, Trigger: "Passive") });
        var s = Sample(relics: new List<string> { "h1" });
        Assert.Equal(15, PassiveModifiers.ApplyPassiveRestHealBonus(10, s, fake));
    }
}
