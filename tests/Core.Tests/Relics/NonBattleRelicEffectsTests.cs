using System.Collections.Generic;
using System.Collections.Immutable;
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
}
