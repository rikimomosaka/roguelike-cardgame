using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class ActStartActionsTests
{
    [Fact]
    public void GenerateChoices_Returns3DistinctRelicsFromActPool()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat);
        var choice = ActStartActions.GenerateChoices(s, act: 1, cat, new SystemRng(42));
        Assert.Equal(3, choice.RelicIds.Length);
        Assert.Equal(3, choice.RelicIds.Distinct().Count());
        var pool = cat.ActStartRelicPools![1];
        foreach (var id in choice.RelicIds) Assert.Contains(id, pool);
    }

    [Fact]
    public void GenerateChoices_ExcludesOwnedRelics()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var pool = cat.ActStartRelicPools![1];
        var s = TestRunStates.FreshDefault(cat) with
        {
            Relics = (IReadOnlyList<string>)new[] { pool[0], pool[1] },
        };
        var choice = ActStartActions.GenerateChoices(s, act: 1, cat, new SystemRng(1));
        Assert.DoesNotContain(pool[0], choice.RelicIds);
        Assert.DoesNotContain(pool[1], choice.RelicIds);
    }

    [Fact]
    public void ChooseRelic_AddsRelic_ClearsChoice()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var pool = cat.ActStartRelicPools![1];
        var s = TestRunStates.FreshDefault(cat) with
        {
            ActiveActStartRelicChoice = new ActStartRelicChoice(
                ImmutableArray.Create(pool[0], pool[1], pool[2])),
        };
        var next = ActStartActions.ChooseRelic(s, pool[0], cat);
        Assert.Contains(pool[0], next.Relics);
        Assert.Null(next.ActiveActStartRelicChoice);
    }

    [Fact]
    public void ChooseRelic_InvalidId_Throws()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var pool = cat.ActStartRelicPools![1];
        var s = TestRunStates.FreshDefault(cat) with
        {
            ActiveActStartRelicChoice = new ActStartRelicChoice(
                ImmutableArray.Create(pool[0], pool[1], pool[2])),
        };
        Assert.Throws<ArgumentException>(() =>
            ActStartActions.ChooseRelic(s, "not_in_choice", cat));
    }

    [Fact]
    public void ChooseRelic_AddsToRelicsList()
    {
        // Phase 10.5.L1.5: base relic JSON は effects=[] にリセット済みなので、
        // OnPickup 発火による副作用は base catalog では無く、Relics list への追加だけ確認。
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat) with
        {
            ActiveActStartRelicChoice = new ActStartRelicChoice(
                ImmutableArray.Create("act1_start_01", "act1_start_02", "act1_start_03")),
        };
        var next = ActStartActions.ChooseRelic(s, "act1_start_01", cat);
        Assert.Contains("act1_start_01", next.Relics);
        Assert.Null(next.ActiveActStartRelicChoice);
    }
}
