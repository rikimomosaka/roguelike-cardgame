using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Relics;

public class NonBattleRelicEffectsTests
{
    private static readonly DataCatalog Catalog = EmbeddedDataLoader.LoadCatalog();

    private static RunState Sample(int hp = 50, int maxHp = 80, int gold = 99) =>
        RunState.NewSoloRun(
            Catalog, 1UL, 0,
            ImmutableDictionary<int, TileKind>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            new System.DateTimeOffset(2026, 4, 22, 0, 0, 0, System.TimeSpan.Zero)
        ) with { CurrentHp = hp, MaxHp = maxHp, Gold = gold };

    // Phase 10.5.L1.5: 36 base relic JSON は effects=[] にリセット済みなので、
    // ApplyOnPickup / ApplyOnMapTileResolved / ApplyPassiveRestHealBonus の効果は
    // base relic では発火しない (extra_max_hp / coin_purse / traveler_boots / warm_blanket
    // を含む全 relic の effects=[])。テストは fake relic を catalog 注入で構築する。

    [Fact]
    public void ApplyOnPickup_GainMaxHpEffect_IncreasesMaxHpAndCurrentHp()
    {
        var s0 = Sample(hp: 50, maxHp: 80);
        var fake = BuildCatalogWithFakeRelic(
            id: "fake_max_hp",
            effects: new[] { new CardEffect(
                "gainMaxHp", EffectScope.Self, null, 7, Trigger: "OnPickup") });
        var s1 = NonBattleRelicEffects.ApplyOnPickup(s0, "fake_max_hp", fake);
        Assert.Equal(87, s1.MaxHp);
        Assert.Equal(57, s1.CurrentHp);
    }

    [Fact]
    public void ApplyOnPickup_GainGoldEffect_AddsGold()
    {
        var s0 = Sample(gold: 99);
        var fake = BuildCatalogWithFakeRelic(
            id: "fake_gold",
            effects: new[] { new CardEffect(
                "gainGold", EffectScope.Self, null, 50, Trigger: "OnPickup") });
        var s1 = NonBattleRelicEffects.ApplyOnPickup(s0, "fake_gold", fake);
        Assert.Equal(149, s1.Gold);
    }

    [Fact]
    public void ApplyOnPickup_NonOnPickupTriggerEffect_NoOp()
    {
        var s0 = Sample(gold: 99);
        var fake = BuildCatalogWithFakeRelic(
            id: "fake_tile",
            effects: new[] { new CardEffect(
                "gainGold", EffectScope.Self, null, 50, Trigger: "OnMapTileResolved") });
        var s1 = NonBattleRelicEffects.ApplyOnPickup(s0, "fake_tile", fake);
        Assert.Equal(99, s1.Gold);
    }

    [Fact]
    public void ApplyOnMapTileResolved_GainGoldEffect_GrantsOneGoldPerOwned()
    {
        var fake = BuildCatalogWithFakeRelic(
            id: "fake_walker",
            effects: new[] { new CardEffect(
                "gainGold", EffectScope.Self, null, 1, Trigger: "OnMapTileResolved") });
        var s0 = Sample(gold: 10) with { Relics = new List<string> { "fake_walker" } };
        var s1 = NonBattleRelicEffects.ApplyOnMapTileResolved(s0, fake);
        Assert.Equal(11, s1.Gold);
    }

    [Fact]
    public void ApplyOnMapTileResolved_NoMatchingRelic_NoOp()
    {
        var s0 = Sample(gold: 10);
        var s1 = NonBattleRelicEffects.ApplyOnMapTileResolved(s0, Catalog);
        Assert.Equal(10, s1.Gold);
    }

    [Fact]
    public void ApplyPassiveRestHealBonus_PassiveRestHealBonusEffect_AddsBonus()
    {
        var fake = BuildCatalogWithFakeRelic(
            id: "fake_warm",
            effects: new[] { new CardEffect(
                "restHealBonus", EffectScope.Self, null, 10, Trigger: "Passive") });
        var s0 = Sample() with { Relics = new List<string> { "fake_warm" } };
        int bonus = NonBattleRelicEffects.ApplyPassiveRestHealBonus(0, s0, fake);
        Assert.Equal(10, bonus);
    }

    [Fact]
    public void ApplyPassiveRestHealBonus_NoRelic_ReturnsBase()
    {
        var s0 = Sample();
        int bonus = NonBattleRelicEffects.ApplyPassiveRestHealBonus(5, s0, Catalog);
        Assert.Equal(5, bonus);
    }

    [Fact]
    public void ApplyOnPickup_NotImplementedRelic_NoOp()
    {
        var s0 = Sample(gold: 99);
        var fake = BuildCatalogWithFakeRelic(
            id: "fake_unimpl_pickup",
            effects: new[] { new CardEffect(
                "gainGold", EffectScope.Self, null, 50, Trigger: "OnPickup") },
            implemented: false);
        var s1 = NonBattleRelicEffects.ApplyOnPickup(s0, "fake_unimpl_pickup", fake);
        Assert.Equal(99, s1.Gold);
    }

    [Fact]
    public void ApplyOnMapTileResolved_NotImplementedRelic_NoOp()
    {
        var s0 = Sample(gold: 10) with { Relics = new List<string> { "fake_unimpl_tile" } };
        var fake = BuildCatalogWithFakeRelic(
            id: "fake_unimpl_tile",
            effects: new[] { new CardEffect(
                "gainGold", EffectScope.Self, null, 1, Trigger: "OnMapTileResolved") },
            implemented: false);
        var s1 = NonBattleRelicEffects.ApplyOnMapTileResolved(s0, fake);
        Assert.Equal(10, s1.Gold);
    }

    [Fact]
    public void ApplyPassiveRestHealBonus_NotImplementedRelic_NoOp()
    {
        var s0 = Sample() with { Relics = new List<string> { "fake_unimpl_passive" } };
        var fake = BuildCatalogWithFakeRelic(
            id: "fake_unimpl_passive",
            effects: new[] { new CardEffect(
                "restHealBonus", EffectScope.Self, null, 10, Trigger: "Passive") },
            implemented: false);
        int bonus = NonBattleRelicEffects.ApplyPassiveRestHealBonus(0, s0, fake);
        Assert.Equal(0, bonus);
    }

    [Fact]
    public void ApplyOnPickup_MultipleEffectsWithMixedTriggers_AppliesOnlyOnPickup()
    {
        // Phase 10.5.L1.5: 1 個の relic に複数 trigger の effect を持たせられる
        var s0 = Sample(hp: 50, maxHp: 80, gold: 0);
        var fake = BuildCatalogWithFakeRelic(
            id: "fake_multi",
            effects: new[]
            {
                new CardEffect("gainMaxHp", EffectScope.Self, null, 5, Trigger: "OnPickup"),
                new CardEffect("gainGold",  EffectScope.Self, null, 99, Trigger: "OnMapTileResolved"),
            });
        var s1 = NonBattleRelicEffects.ApplyOnPickup(s0, "fake_multi", fake);
        Assert.Equal(85, s1.MaxHp);
        Assert.Equal(55, s1.CurrentHp);
        Assert.Equal(0, s1.Gold);
    }

    private static DataCatalog BuildCatalogWithFakeRelic(
        string id,
        IReadOnlyList<CardEffect> effects,
        bool implemented = true)
    {
        var fake = new RelicDefinition(
            Id: id,
            Name: $"fake_{id}",
            Rarity: CardRarity.Common,
            Effects: effects,
            Description: "",
            Implemented: implemented);

        var orig = Catalog;
        var relics = orig.Relics.ToDictionary(kv => kv.Key, kv => kv.Value);
        relics[id] = fake;
        return orig with { Relics = relics };
    }
}
