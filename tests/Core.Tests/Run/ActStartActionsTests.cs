using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;
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
    public void GenerateChoices_LegacySaveWithTooManyOwnedActRelics_FillsMissingChoicesFromNormalPool()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var pool = cat.ActStartRelicPools![2];
        var owned = pool.Take(4).ToArray();
        var s = TestRunStates.FreshDefault(cat) with
        {
            CurrentAct = 2,
            Relics = owned,
        };

        var choice = ActStartActions.GenerateChoices(s, act: 2, cat, new SystemRng(7));

        Assert.Equal(3, choice.RelicIds.Length);
        Assert.Equal(3, choice.RelicIds.Distinct().Count());
        Assert.Contains(pool[4], choice.RelicIds);
        Assert.DoesNotContain(choice.RelicIds, id => owned.Contains(id));
        var normal = RelicPoolHelper.GetNormalAcquisitionRelics(cat)
            .Select(relic => relic.Id)
            .ToHashSet(System.StringComparer.Ordinal);
        Assert.Equal(2, choice.RelicIds.Count(normal.Contains));
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
    public void ChooseRelic_AppliesActTwoPickupEffect()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat) with
        {
            ActiveActStartRelicChoice = new ActStartRelicChoice(
                ImmutableArray.Create("act2_start_01", "act2_start_02", "act2_start_03")),
            CurrentHp = 40,
            MaxHp = 80,
        };
        var next = ActStartActions.ChooseRelic(s, "act2_start_01", cat);
        Assert.Contains("act2_start_01", next.Relics);
        Assert.Null(next.ActiveActStartRelicChoice);
        Assert.Equal(92, next.MaxHp);
        Assert.Equal(52, next.CurrentHp);
    }
}
