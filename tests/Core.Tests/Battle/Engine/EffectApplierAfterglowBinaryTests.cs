using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public sealed class EffectApplierAfterglowBinaryTests
{
    [Fact]
    public void Positive_afterglow_application_is_capped_at_one_and_does_not_stack()
    {
        var state = BattleFixtures.MinimalState();
        var effect = new CardEffect(
            "buff",
            EffectScope.Self,
            null,
            5,
            Name: BattleStatusIds.Afterglow);

        var (first, firstEvents) = EffectApplier.Apply(
            state,
            state.Allies[0],
            effect,
            new FakeRng([], []),
            BattleFixtures.MinimalCatalog());
        var (second, secondEvents) = EffectApplier.Apply(
            first,
            first.Allies[0],
            effect,
            new FakeRng([], []),
            BattleFixtures.MinimalCatalog());

        Assert.Equal(1, first.Allies[0].GetStatus(BattleStatusIds.Afterglow));
        Assert.Equal(1, second.Allies[0].GetStatus(BattleStatusIds.Afterglow));
        Assert.Contains(firstEvents, battleEvent =>
            battleEvent.Kind == BattleEventKind.ApplyStatus
            && battleEvent.Note == BattleStatusIds.Afterglow
            && battleEvent.Amount == 1);
        Assert.DoesNotContain(secondEvents, battleEvent =>
            battleEvent.Kind == BattleEventKind.ApplyStatus
            && battleEvent.Note == BattleStatusIds.Afterglow);
    }

    [Fact]
    public void Resonance_propagation_caps_afterglow_at_one_for_every_recipient()
    {
        var source = BattleFixtures.Hero() with
        {
            Statuses = ImmutableDictionary<string, int>.Empty
                .Add(BattleStatusIds.Resonance, 1),
        };
        var alreadyHasAfterglow = BattleFixtures.SummonActor(
            "existing",
            "minion",
            1) with
        {
            Statuses = ImmutableDictionary<string, int>.Empty
                .Add(BattleStatusIds.Afterglow, 1),
        };
        var freshRecipient = BattleFixtures.SummonActor(
            "fresh",
            "minion",
            2);
        var state = BattleFixtures.MinimalState(
            allies: [source, alreadyHasAfterglow, freshRecipient]);
        var effect = new CardEffect(
            "buff",
            EffectScope.Self,
            null,
            7,
            Name: BattleStatusIds.Afterglow);

        var (after, events) = EffectApplier.Apply(
            state,
            source,
            effect,
            new FakeRng([], []),
            BattleFixtures.MinimalCatalog());

        Assert.All(after.Allies, actor =>
            Assert.Equal(1, actor.GetStatus(BattleStatusIds.Afterglow)));
        Assert.Equal(0, after.Allies[0].GetStatus(BattleStatusIds.Resonance));
        Assert.DoesNotContain(events, battleEvent =>
            battleEvent.TargetInstanceId == alreadyHasAfterglow.InstanceId
            && battleEvent.Note == BattleStatusIds.Afterglow);
        Assert.Contains(events, battleEvent =>
            battleEvent.TargetInstanceId == freshRecipient.InstanceId
            && battleEvent.Note == BattleStatusIds.Afterglow
            && battleEvent.Amount == 1);
    }
}
