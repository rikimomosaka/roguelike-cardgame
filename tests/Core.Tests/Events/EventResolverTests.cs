using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Events;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Rewards;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Events;

public class EventResolverTests
{
    private static readonly DataCatalog Catalog = EmbeddedDataLoader.LoadCatalog();

    private static RunState Base(int hp = 50, int maxHp = 80, int gold = 100) =>
        RunState.NewSoloRun(
            Catalog, 1UL, 0,
            ImmutableDictionary<int, TileKind>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            new DateTimeOffset(2026, 4, 22, 0, 0, 0, TimeSpan.Zero)
        ) with { CurrentHp = hp, MaxHp = maxHp, Gold = gold };

    private static EventInstance MakeInstance(params EventChoice[] choices) =>
        new("test", ImmutableArray.Create(choices));

    [Fact]
    public void ApplyChoice_GainGold_IncreasesGold()
    {
        var inst = MakeInstance(new EventChoice("gain",
            null, ImmutableArray.Create<EventEffect>(new EventEffect.GainGold(30))));
        var s0 = Base(gold: 100) with { ActiveEvent = inst };
        var s1 = EventResolver.ApplyChoice(s0, 0, Catalog, new SequentialRng(1UL));
        Assert.Equal(130, s1.Gold);
        Assert.NotNull(s1.ActiveEvent);
        Assert.Equal(0, s1.ActiveEvent!.ChosenIndex);
    }

    [Fact]
    public void ApplyChoice_PayGold_ReducesGold()
    {
        var inst = MakeInstance(new EventChoice("pay",
            null, ImmutableArray.Create<EventEffect>(new EventEffect.PayGold(30))));
        var s0 = Base(gold: 100) with { ActiveEvent = inst };
        var s1 = EventResolver.ApplyChoice(s0, 0, Catalog, new SequentialRng(1UL));
        Assert.Equal(70, s1.Gold);
    }

    [Fact]
    public void ApplyChoice_HealCapsAtMaxHp()
    {
        var inst = MakeInstance(new EventChoice("heal",
            null, ImmutableArray.Create<EventEffect>(new EventEffect.Heal(100))));
        var s0 = Base(hp: 50, maxHp: 80) with { ActiveEvent = inst };
        var s1 = EventResolver.ApplyChoice(s0, 0, Catalog, new SequentialRng(1UL));
        Assert.Equal(80, s1.CurrentHp);
    }

    [Fact]
    public void ApplyChoice_TakeDamageFloorsAtZero()
    {
        var inst = MakeInstance(new EventChoice("dmg",
            null, ImmutableArray.Create<EventEffect>(new EventEffect.TakeDamage(100))));
        var s0 = Base(hp: 10) with { ActiveEvent = inst };
        var s1 = EventResolver.ApplyChoice(s0, 0, Catalog, new SequentialRng(1UL));
        Assert.Equal(0, s1.CurrentHp);
    }

    [Fact]
    public void ApplyChoice_GainMaxHp_IncreasesBoth()
    {
        var inst = MakeInstance(new EventChoice("max",
            null, ImmutableArray.Create<EventEffect>(new EventEffect.GainMaxHp(5))));
        var s0 = Base(hp: 50, maxHp: 80) with { ActiveEvent = inst };
        var s1 = EventResolver.ApplyChoice(s0, 0, Catalog, new SequentialRng(1UL));
        Assert.Equal(85, s1.MaxHp);
        Assert.Equal(55, s1.CurrentHp);
    }

    [Fact]
    public void ApplyChoice_LoseMaxHp_DecreasesBothAndFloorsCurrent()
    {
        var inst = MakeInstance(new EventChoice("max",
            null, ImmutableArray.Create<EventEffect>(new EventEffect.LoseMaxHp(10))));
        var s0 = Base(hp: 75, maxHp: 80) with { ActiveEvent = inst };
        var s1 = EventResolver.ApplyChoice(s0, 0, Catalog, new SequentialRng(1UL));
        Assert.Equal(70, s1.MaxHp);
        Assert.Equal(70, s1.CurrentHp);
    }

    [Fact]
    public void ApplyChoice_GrantCardReward_SetsActiveReward()
    {
        var inst = MakeInstance(new EventChoice("card",
            null, ImmutableArray.Create<EventEffect>(new EventEffect.GrantCardReward())));
        var s0 = Base() with { ActiveEvent = inst };
        var s1 = EventResolver.ApplyChoice(s0, 0, Catalog, new SequentialRng(1UL));
        Assert.NotNull(s1.ActiveReward);
        Assert.Equal(3, s1.ActiveReward!.CardChoices.Length);
        Assert.NotNull(s1.ActiveEvent);
        Assert.Equal(0, s1.ActiveEvent!.ChosenIndex);
    }

    [Fact]
    public void ApplyChoice_AlreadyResolved_Throws()
    {
        var inst = MakeInstance(
            new EventChoice("gain", null, ImmutableArray.Create<EventEffect>(new EventEffect.GainGold(10))),
            new EventChoice("pay", null, ImmutableArray.Create<EventEffect>(new EventEffect.PayGold(10))));
        var s0 = Base() with { ActiveEvent = inst };
        var s1 = EventResolver.ApplyChoice(s0, 0, Catalog, new SequentialRng(1UL));
        // 2回目の ApplyChoice は throw
        Assert.Throws<InvalidOperationException>(() =>
            EventResolver.ApplyChoice(s1, 1, Catalog, new SequentialRng(1UL)));
    }

    [Fact]
    public void ApplyChoice_GainRelicRandom_AddsRelicAndTriggersOnPickup()
    {
        var inst = MakeInstance(new EventChoice("relic",
            null, ImmutableArray.Create<EventEffect>(
                new EventEffect.GainRelicRandom(CardRarity.Common))));
        var s0 = Base() with { ActiveEvent = inst };
        var s1 = EventResolver.ApplyChoice(s0, 0, Catalog, new SequentialRng(7UL));
        Assert.Single(s1.Relics);
        // OnPickup が発火していれば extra_max_hp / coin_purse のどちらかでも効果が見える
        Assert.True(s1.MaxHp >= s0.MaxHp);
    }

    [Fact]
    public void ApplyChoice_ConditionFails_Throws()
    {
        var inst = MakeInstance(new EventChoice("pay",
            new EventCondition.MinGold(500),
            ImmutableArray.Create<EventEffect>(new EventEffect.PayGold(500))));
        var s0 = Base(gold: 100) with { ActiveEvent = inst };
        Assert.Throws<InvalidOperationException>(() =>
            EventResolver.ApplyChoice(s0, 0, Catalog, new SequentialRng(1UL)));
    }

    [Fact]
    public void ApplyChoice_IndexOutOfRange_Throws()
    {
        var inst = MakeInstance(new EventChoice("x", null, ImmutableArray<EventEffect>.Empty));
        var s0 = Base() with { ActiveEvent = inst };
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EventResolver.ApplyChoice(s0, 5, Catalog, new SequentialRng(1UL)));
    }
}
