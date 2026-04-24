using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Events;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Events;

public class EventPoolTests
{
    private static readonly DataCatalog Cat = EmbeddedDataLoader.LoadCatalog();

    private static RunState BaseState(int gold = 100) => RunState.NewSoloRun(
        Cat, 1UL, 0,
        ImmutableDictionary<int, TileKind>.Empty,
        ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
        ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
        new DateTimeOffset(2026, 4, 25, 0, 0, 0, TimeSpan.Zero)
    ) with { Gold = gold };

    private static EventDefinition Def(
        string id,
        ImmutableArray<int> tiers,
        EventRarity rarity = EventRarity.Common,
        EventCondition? condition = null)
        => new(id, id, "", ImmutableArray<EventChoice>.Empty, tiers, rarity, condition);

    [Fact]
    public void Pick_DeterministicForSameSeed()
    {
        var defs = ImmutableArray.Create(
            Def("a", ImmutableArray.Create(1)),
            Def("b", ImmutableArray.Create(1)),
            Def("c", ImmutableArray.Create(1)));
        var rngA = new SequentialRng(42UL);
        var rngB = new SequentialRng(42UL);
        Assert.Equal(
            EventPool.Pick(defs, 1, BaseState(), rngA).Id,
            EventPool.Pick(defs, 1, BaseState(), rngB).Id);
    }

    [Fact]
    public void Pick_EmptyPool_Throws()
    {
        var rng = new SequentialRng(1UL);
        Assert.Throws<InvalidOperationException>(() =>
            EventPool.Pick(ImmutableArray<EventDefinition>.Empty, 1, BaseState(), rng));
    }

    [Fact]
    public void Pick_FiltersByTier()
    {
        var defs = ImmutableArray.Create(
            Def("tier1_only", ImmutableArray.Create(1)),
            Def("tier2_only", ImmutableArray.Create(2)));
        var rng = new SequentialRng(1UL);
        var picked = EventPool.Pick(defs, 2, BaseState(), rng);
        Assert.Equal("tier2_only", picked.Id);
    }

    [Fact]
    public void Pick_ExcludesEmptyTiersFromPool()
    {
        var defs = ImmutableArray.Create(
            Def("no_tiers", ImmutableArray<int>.Empty),
            Def("tier1", ImmutableArray.Create(1)));
        var rng = new SequentialRng(1UL);
        var picked = EventPool.Pick(defs, 1, BaseState(), rng);
        Assert.Equal("tier1", picked.Id);
    }

    [Fact]
    public void Pick_FiltersByEventLevelCondition()
    {
        var defs = ImmutableArray.Create(
            Def("rich", ImmutableArray.Create(1), condition: new EventCondition.MinGold(500)),
            Def("poor_ok", ImmutableArray.Create(1)));
        var rng = new SequentialRng(1UL);
        var picked = EventPool.Pick(defs, 1, BaseState(gold: 10), rng);
        Assert.Equal("poor_ok", picked.Id);
    }

    [Fact]
    public void Pick_NoCandidates_Throws()
    {
        var defs = ImmutableArray.Create(
            Def("tier2", ImmutableArray.Create(2)));
        var rng = new SequentialRng(1UL);
        Assert.Throws<InvalidOperationException>(() =>
            EventPool.Pick(defs, 1, BaseState(), rng));
    }

    [Fact]
    public void Pick_RarityWeightsAffectDistribution()
    {
        var defs = ImmutableArray.Create(
            Def("common_a", ImmutableArray.Create(1), EventRarity.Common),
            Def("rare_a", ImmutableArray.Create(1), EventRarity.Rare));
        int commonCount = 0;
        int rareCount = 0;
        for (ulong seed = 0; seed < 200; seed++)
        {
            var rng = new SequentialRng(seed);
            var picked = EventPool.Pick(defs, 1, BaseState(), rng);
            if (picked.Id == "common_a") commonCount++;
            else if (picked.Id == "rare_a") rareCount++;
        }
        Assert.True(commonCount > rareCount,
            $"Common should outweigh Rare (common={commonCount}, rare={rareCount})");
    }
}
