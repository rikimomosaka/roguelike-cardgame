using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public sealed class ResonanceRuntimeTests
{
    [Fact]
    public void Self_buff_group_spreads_to_every_other_living_ally_and_spends_one_stack()
    {
        var source = BattleFixtures.Hero() with
        {
            Statuses = ImmutableDictionary<string, int>.Empty
                .Add(BattleStatusIds.Resonance, 2),
        };
        var rear = BattleFixtures.SummonActor("rear", "minion", 2);
        var front = BattleFixtures.SummonActor("front", "minion", 1);
        var state = BattleFixtures.MinimalState(allies: [source, rear, front]);
        var effect = new CardEffect(
            "buff",
            EffectScope.Self,
            null,
            0,
            StatusChanges:
            [
                new StatusChange(BattleStatusIds.Strength, 2),
                new StatusChange(BattleStatusIds.Vigor, 1),
            ]);

        var (after, events) = EffectApplier.Apply(
            state,
            source,
            effect,
            new FakeRng([], []),
            BattleFixtures.MinimalCatalog());

        Assert.All(after.Allies, actor =>
        {
            Assert.Equal(2, actor.GetStatus(BattleStatusIds.Strength));
            Assert.Equal(1, actor.GetStatus(BattleStatusIds.Vigor));
        });
        Assert.Equal(1, after.Allies[0].GetStatus(BattleStatusIds.Resonance));
        Assert.Equal(
            [
                (source.InstanceId, BattleStatusIds.Strength),
                (source.InstanceId, BattleStatusIds.Vigor),
                (front.InstanceId, BattleStatusIds.Strength),
                (front.InstanceId, BattleStatusIds.Vigor),
                (rear.InstanceId, BattleStatusIds.Strength),
                (rear.InstanceId, BattleStatusIds.Vigor),
                (source.InstanceId, BattleStatusIds.Resonance),
            ],
            events.Select(battleEvent =>
                (battleEvent.TargetInstanceId!, battleEvent.Note!)));
        Assert.Equal(-1, events[^1].Amount);
    }

    [Fact]
    public void Resonance_itself_is_not_spread_and_recipient_resonance_does_not_recurse()
    {
        var source = BattleFixtures.Hero() with
        {
            Statuses = ImmutableDictionary<string, int>.Empty
                .Add(BattleStatusIds.Resonance, 1),
        };
        var receiver = BattleFixtures.SummonActor("receiver", "minion", 1) with
        {
            Statuses = ImmutableDictionary<string, int>.Empty
                .Add(BattleStatusIds.Resonance, 3),
        };
        var third = BattleFixtures.SummonActor("third", "minion", 2);
        var state = BattleFixtures.MinimalState(
            allies: [source, receiver, third]);
        var effect = new CardEffect(
            "buff",
            EffectScope.Self,
            null,
            0,
            StatusChanges:
            [
                new StatusChange(BattleStatusIds.Resonance, 2),
                new StatusChange(BattleStatusIds.Strength, 1),
            ]);

        var (after, events) = EffectApplier.Apply(
            state,
            source,
            effect,
            new FakeRng([], []),
            BattleFixtures.MinimalCatalog());

        Assert.Equal(2, after.Allies[0].GetStatus(BattleStatusIds.Resonance));
        Assert.Equal(3, after.Allies[1].GetStatus(BattleStatusIds.Resonance));
        Assert.Equal(0, after.Allies[2].GetStatus(BattleStatusIds.Resonance));
        Assert.Equal(1, after.Allies[1].GetStatus(BattleStatusIds.Strength));
        Assert.Equal(1, after.Allies[2].GetStatus(BattleStatusIds.Strength));
        Assert.Equal(2, events.Count(battleEvent =>
            battleEvent.Note == BattleStatusIds.Strength
            && battleEvent.TargetInstanceId != source.InstanceId));
    }

    [Fact]
    public void Resonance_is_spent_when_every_living_recipient_is_already_capped()
    {
        var source = BattleFixtures.Hero() with
        {
            Statuses = ImmutableDictionary<string, int>.Empty
                .Add(BattleStatusIds.Resonance, 2),
        };
        var capped = BattleFixtures.SummonActor("capped", "minion", 1) with
        {
            Statuses = ImmutableDictionary<string, int>.Empty
                .Add(BattleStatusIds.Strength, int.MaxValue),
        };
        var dead = BattleFixtures.SummonActor("dead", "minion", 2) with
        {
            CurrentHp = 0,
        };
        var state = BattleFixtures.MinimalState(allies: [source, capped, dead]);
        var effect = new CardEffect(
            "buff",
            EffectScope.Self,
            null,
            1,
            Name: BattleStatusIds.Strength);

        var (after, events) = EffectApplier.Apply(
            state,
            source,
            effect,
            new FakeRng([], []),
            BattleFixtures.MinimalCatalog());

        Assert.Equal(1, after.Allies[0].GetStatus(BattleStatusIds.Resonance));
        Assert.Contains(events, battleEvent =>
            battleEvent.Note == BattleStatusIds.Resonance
            && battleEvent.TargetInstanceId == source.InstanceId
            && battleEvent.Amount == -1);
    }

    [Fact]
    public void Resonance_is_spent_after_an_actual_self_buff_even_without_a_recipient()
    {
        var source = BattleFixtures.Hero() with
        {
            Statuses = ImmutableDictionary<string, int>.Empty
                .Add(BattleStatusIds.Resonance, 1),
        };
        var state = BattleFixtures.MinimalState(allies: [source]);
        var effect = new CardEffect(
            "buff",
            EffectScope.Self,
            null,
            1,
            Name: BattleStatusIds.Strength);

        var (after, events) = EffectApplier.Apply(
            state,
            source,
            effect,
            new FakeRng([], []),
            BattleFixtures.MinimalCatalog());

        Assert.Equal(1, after.Allies[0].GetStatus(BattleStatusIds.Strength));
        Assert.Equal(0, after.Allies[0].GetStatus(BattleStatusIds.Resonance));
        Assert.Contains(events, battleEvent =>
            battleEvent.Kind == BattleEventKind.RemoveStatus
            && battleEvent.Note == BattleStatusIds.Resonance
            && battleEvent.Amount == -1);
    }

    [Fact]
    public void Ally_targeted_buff_does_not_trigger_resonance()
    {
        var source = BattleFixtures.Hero() with
        {
            Statuses = ImmutableDictionary<string, int>.Empty
                .Add(BattleStatusIds.Resonance, 1),
        };
        var ally = BattleFixtures.SummonActor("ally", "minion", 1);
        var state = BattleFixtures.MinimalState(allies: [source, ally]) with
        {
            TargetAllyIndex = 1,
        };
        var effect = new CardEffect(
            "buff",
            EffectScope.Single,
            EffectSide.Ally,
            2,
            Name: BattleStatusIds.Strength);

        var (after, _) = EffectApplier.Apply(
            state,
            source,
            effect,
            new FakeRng([], []),
            BattleFixtures.MinimalCatalog());

        Assert.Equal(0, after.Allies[0].GetStatus(BattleStatusIds.Strength));
        Assert.Equal(2, after.Allies[1].GetStatus(BattleStatusIds.Strength));
        Assert.Equal(1, after.Allies[0].GetStatus(BattleStatusIds.Resonance));
    }

    [Fact]
    public void Growth_self_buff_triggers_resonance_once_without_recursion()
    {
        var source = BattleFixtures.Hero() with
        {
            Statuses = ImmutableDictionary<string, int>.Empty
                .Add(BattleStatusIds.Growth, 1)
                .Add(BattleStatusIds.Resonance, 1),
        };
        var receiver = BattleFixtures.SummonActor("receiver", "minion", 1) with
        {
            // A copied buff is a direct propagation, not another Resonance
            // trigger owned by its recipient.
            Statuses = ImmutableDictionary<string, int>.Empty
                .Add(BattleStatusIds.Resonance, 2),
        };
        var state = BattleFixtures.MinimalState(allies: [source, receiver]);

        var (after, events) = TurnStartProcessor.Process(
            state,
            new FakeRng([], []),
            BattleFixtures.MinimalCatalog());

        Assert.Equal(1, after.Allies[0].GetStatus(BattleStatusIds.Strength));
        Assert.Equal(1, after.Allies[1].GetStatus(BattleStatusIds.Strength));
        Assert.Equal(0, after.Allies[0].GetStatus(BattleStatusIds.Resonance));
        Assert.Equal(2, after.Allies[1].GetStatus(BattleStatusIds.Resonance));
        Assert.Equal(2, events.Count(battleEvent =>
            battleEvent.Kind == BattleEventKind.ApplyStatus
            && battleEvent.Note == BattleStatusIds.Strength));
        Assert.Single(events, battleEvent =>
            battleEvent.Note == BattleStatusIds.Resonance
            && battleEvent.Amount == -1);
    }
}
