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

    [Fact]
    public void ApplyOnPickup_ExtraMaxHp_IncreasesMaxHpAndCurrentHp()
    {
        var s0 = Sample(hp: 50, maxHp: 80);
        var s1 = NonBattleRelicEffects.ApplyOnPickup(s0, "extra_max_hp", Catalog);
        Assert.Equal(87, s1.MaxHp);
        Assert.Equal(57, s1.CurrentHp);
    }

    [Fact]
    public void ApplyOnPickup_CoinPurse_AddsGold()
    {
        var s0 = Sample(gold: 99);
        var s1 = NonBattleRelicEffects.ApplyOnPickup(s0, "coin_purse", Catalog);
        Assert.Equal(149, s1.Gold);
    }

    [Fact]
    public void ApplyOnPickup_NonOnPickupTrigger_NoOp()
    {
        var s0 = Sample(gold: 99);
        var s1 = NonBattleRelicEffects.ApplyOnPickup(s0, "traveler_boots", Catalog);
        Assert.Equal(99, s1.Gold);
    }

    [Fact]
    public void ApplyOnMapTileResolved_TravelerBoots_GrantsOneGoldPerOwned()
    {
        var s0 = Sample(gold: 10) with { Relics = new List<string> { "traveler_boots" } };
        var s1 = NonBattleRelicEffects.ApplyOnMapTileResolved(s0, Catalog);
        Assert.Equal(11, s1.Gold);
    }

    [Fact]
    public void ApplyOnMapTileResolved_NoTravelerBoots_NoOp()
    {
        var s0 = Sample(gold: 10);
        var s1 = NonBattleRelicEffects.ApplyOnMapTileResolved(s0, Catalog);
        Assert.Equal(10, s1.Gold);
    }

    [Fact]
    public void ApplyPassiveRestHealBonus_WarmBlanket_Adds10()
    {
        var s0 = Sample() with { Relics = new List<string> { "warm_blanket" } };
        int bonus = NonBattleRelicEffects.ApplyPassiveRestHealBonus(0, s0, Catalog);
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
        var fakeCatalog = BuildCatalogWithFakeRelic(
            id: "fake_unimpl_pickup",
            trigger: RelicTrigger.OnPickup,
            effects: new[] { new CardEffect(
                "gainGold", EffectScope.Self, null, 50) },
            implemented: false);
        var s1 = NonBattleRelicEffects.ApplyOnPickup(s0, "fake_unimpl_pickup", fakeCatalog);
        Assert.Equal(99, s1.Gold);
    }

    [Fact]
    public void ApplyOnMapTileResolved_NotImplementedRelic_NoOp()
    {
        var s0 = Sample(gold: 10) with { Relics = new List<string> { "fake_unimpl_tile" } };
        var fakeCatalog = BuildCatalogWithFakeRelic(
            id: "fake_unimpl_tile",
            trigger: RelicTrigger.OnMapTileResolved,
            effects: new[] { new CardEffect(
                "gainGold", EffectScope.Self, null, 1) },
            implemented: false);
        var s1 = NonBattleRelicEffects.ApplyOnMapTileResolved(s0, fakeCatalog);
        Assert.Equal(10, s1.Gold);
    }

    [Fact]
    public void ApplyPassiveRestHealBonus_NotImplementedRelic_NoOp()
    {
        var s0 = Sample() with { Relics = new List<string> { "fake_unimpl_passive" } };
        var fakeCatalog = BuildCatalogWithFakeRelic(
            id: "fake_unimpl_passive",
            trigger: RelicTrigger.Passive,
            effects: new[] { new CardEffect(
                "restHealBonus", EffectScope.Self, null, 10) },
            implemented: false);
        int bonus = NonBattleRelicEffects.ApplyPassiveRestHealBonus(0, s0, fakeCatalog);
        Assert.Equal(0, bonus);
    }

    private static DataCatalog BuildCatalogWithFakeRelic(
        string id, RelicTrigger trigger,
        IReadOnlyList<CardEffect> effects,
        bool implemented)
    {
        var fake = new RelicDefinition(
            Id: id,
            Name: $"fake_{id}",
            Rarity: CardRarity.Common,
            Trigger: trigger,
            Effects: effects,
            Description: "",
            Implemented: implemented);

        var orig = Catalog;
        var relics = orig.Relics.ToDictionary(kv => kv.Key, kv => kv.Value);
        relics[id] = fake;
        return orig with { Relics = relics };
    }
}
