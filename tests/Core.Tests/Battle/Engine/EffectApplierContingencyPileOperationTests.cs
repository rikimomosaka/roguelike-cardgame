using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public sealed class EffectApplierContingencyPileOperationTests
{
    private static CardDefinition Tactic(string id) =>
        new(
            id,
            id,
            null,
            CardRarity.Common,
            CardType.Skill,
            Cost: 1,
            UpgradedCost: 0,
            Effects: [new CardEffect("block", EffectScope.Self, null, 2)],
            UpgradedEffects: [new CardEffect("block", EffectScope.Self, null, 4)],
            Keywords: [],
            UpgradedKeywords: [],
            Abilities:
            [
                new ContingencyAbilityDefinition(
                    new ContingencyTriggerDefinition(
                        ContingencyTriggerTiming.TurnStart)),
            ],
            UpgradedAbilities:
            [
                new ContingencyAbilityDefinition(
                    new ContingencyTriggerDefinition(
                        ContingencyTriggerTiming.TurnStart)),
            ]);

    private static BattleCardInstance Card(string id, string definitionId) =>
        new(id, definitionId, IsUpgraded: false, CostOverride: null);

    private static ArmedContingency Armed(string cardInstanceId, long ordinal = 1) =>
        new(
            cardInstanceId,
            "hero_inst",
            new ContingencyTriggerDefinition(
                ContingencyTriggerTiming.TurnStart),
            ordinal);

    private static BattleState State(
        ImmutableArray<BattleCardInstance> exhaust,
        params ArmedContingency[] armed) =>
        BattleFixtures.MinimalState() with
        {
            ExhaustPile = exhaust,
            ArmedContingencies = armed.ToImmutableArray(),
        };

    private static DataCatalog Catalog(params CardDefinition[] cards) =>
        BattleFixtures.MinimalCatalog(cards: cards);

    private static IRng Rng(params int[] values) =>
        new FakeRng(values, []);

    [Fact]
    public void Upgrade_in_exhaust_keeps_the_contingency_armed()
    {
        var definition = Tactic("tactic");
        var card = Card("armed", definition.Id);
        var marker = Armed(card.InstanceId);
        var state = State([card], marker);
        var effect = new CardEffect(
            "upgrade",
            EffectScope.Self,
            null,
            1,
            Pile: "exhaust",
            Select: "all");

        var (after, events) = EffectApplier.Apply(
            state,
            state.Allies[0],
            effect,
            Rng(),
            Catalog(definition));

        Assert.True(Assert.Single(after.ExhaustPile).IsUpgraded);
        Assert.Equal(marker, Assert.Single(after.ArmedContingencies));
        Assert.Equal(BattleEventKind.Upgrade, Assert.Single(events).Kind);
    }

    [Fact]
    public void Upgrade_choose_lists_and_updates_an_armed_exhaust_card()
    {
        var firstDefinition = Tactic("first");
        var secondDefinition = Tactic("second");
        var armedCard = Card("armed", firstDefinition.Id);
        var otherCard = Card("other", secondDefinition.Id);
        var marker = Armed(armedCard.InstanceId);
        var state = State([armedCard, otherCard], marker);
        var catalog = Catalog(firstDefinition, secondDefinition);
        var effect = new CardEffect(
            "upgrade",
            EffectScope.Self,
            null,
            1,
            Pile: "exhaust",
            Select: "choose");

        Assert.True(EffectApplier.NeedsPlayerChoice(
            state,
            effect,
            catalog,
            playingCardInstanceId: armedCard.InstanceId));
        Assert.Equal(
            new[] { armedCard.InstanceId, otherCard.InstanceId },
            EffectApplier.GetChooseCandidates(
                state,
                effect,
                catalog,
                playingCardInstanceId: armedCard.InstanceId).ToArray());

        var (after, _) = EffectApplier.ApplyChoseEffect(
            state,
            state.Allies[0],
            effect,
            [armedCard.InstanceId],
            Rng(),
            catalog,
            playingCardInstanceId: armedCard.InstanceId);

        Assert.True(after.ExhaustPile[0].IsUpgraded);
        Assert.False(after.ExhaustPile[1].IsUpgraded);
        Assert.Equal(marker, Assert.Single(after.ArmedContingencies));
    }

    [Fact]
    public void Cost_modifier_in_exhaust_keeps_the_contingency_armed()
    {
        var definition = Tactic("tactic");
        var card = Card("armed", definition.Id);
        var marker = Armed(card.InstanceId);
        var state = State([card], marker);
        var effect = new CardEffect(
            "modifyCardCost",
            EffectScope.Self,
            null,
            -1,
            Pile: "exhaust",
            Select: "all",
            CostDuration: "untilPlayed");

        var (after, _) = EffectApplier.Apply(
            state,
            state.Allies[0],
            effect,
            Rng(),
            Catalog(definition),
            playingCardInstanceId: card.InstanceId);

        Assert.Equal(-1, Assert.Single(
            Assert.Single(after.ExhaustPile).EffectiveCostModifiers).Amount);
        Assert.Equal(marker, Assert.Single(after.ArmedContingencies));
    }

    [Fact]
    public void Recover_from_exhaust_moves_an_armed_card_and_disarms_it()
    {
        var definition = Tactic("tactic");
        var card = Card("armed", definition.Id);
        var state = State([card], Armed(card.InstanceId));
        var effect = new CardEffect(
            "recoverFromExhaust",
            EffectScope.Self,
            null,
            1,
            Pile: "discard",
            Select: "all");

        var (after, events) = EffectApplier.Apply(
            state,
            state.Allies[0],
            effect,
            Rng(),
            Catalog(definition));

        Assert.Empty(after.ExhaustPile);
        Assert.Equal(card, Assert.Single(after.DiscardPile));
        Assert.Empty(after.ArmedContingencies);
        Assert.Equal(BattleEventKind.RecoverFromExhaust, Assert.Single(events).Kind);
    }

    [Fact]
    public void Recover_from_exhaust_choose_treats_an_armed_card_as_an_ordinary_candidate()
    {
        var firstDefinition = Tactic("first");
        var secondDefinition = Tactic("second");
        var armedCard = Card("armed", firstDefinition.Id);
        var otherCard = Card("other", secondDefinition.Id);
        var state = State(
            [armedCard, otherCard],
            Armed(armedCard.InstanceId),
            Armed(otherCard.InstanceId, ordinal: 2));
        var catalog = Catalog(firstDefinition, secondDefinition);
        var effect = new CardEffect(
            "recoverFromExhaust",
            EffectScope.Self,
            null,
            1,
            Pile: "hand",
            Select: "choose");

        var pending = EffectApplier.BuildPendingChoice(
            state,
            effect,
            catalog,
            playingCardInstanceId: armedCard.InstanceId);
        Assert.Equal("exhaust", pending.Pile);
        Assert.Equal(
            new[] { armedCard.InstanceId, otherCard.InstanceId },
            pending.CandidateInstanceIds.ToArray());

        var (after, _) = EffectApplier.ApplyChoseEffect(
            state,
            state.Allies[0],
            effect,
            [armedCard.InstanceId],
            Rng(),
            catalog,
            playingCardInstanceId: armedCard.InstanceId);

        Assert.Equal(armedCard, Assert.Single(after.Hand));
        Assert.Equal(otherCard, Assert.Single(after.ExhaustPile));
        Assert.Equal(
            otherCard.InstanceId,
            Assert.Single(after.ArmedContingencies).CardInstanceId);
    }

    [Fact]
    public void Leaving_and_returning_to_exhaust_in_one_card_resolution_does_not_rearm()
    {
        var tactic = Tactic("tactic");
        var armedCard = Card("armed", tactic.Id);
        var cycle = new CardDefinition(
            "cycle",
            "Cycle",
            null,
            CardRarity.Common,
            CardType.Skill,
            Cost: 0,
            UpgradedCost: null,
            Effects:
            [
                new CardEffect(
                    "recoverFromExhaust",
                    EffectScope.Self,
                    null,
                    99,
                    Pile: "discard",
                    Select: "all"),
                new CardEffect(
                    "recoverFromDiscard",
                    EffectScope.Self,
                    null,
                    99,
                    Pile: "exhaust",
                    Select: "all"),
            ],
            UpgradedEffects: null,
            Keywords: null);
        var state = State([armedCard], Armed(armedCard.InstanceId)) with
        {
            Hand = [Card("cycle-instance", cycle.Id)],
        };

        var (after, _) = BattleEngine.PlayCard(
            state,
            handIndex: 0,
            targetEnemyIndex: null,
            targetAllyIndex: null,
            Rng(),
            Catalog(tactic, cycle));

        Assert.Contains(
            after.ExhaustPile,
            candidate => candidate.InstanceId == armedCard.InstanceId);
        Assert.Empty(after.ArmedContingencies);
    }
}
