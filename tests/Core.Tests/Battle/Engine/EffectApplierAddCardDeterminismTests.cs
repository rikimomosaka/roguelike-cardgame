using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public sealed class EffectApplierAddCardDeterminismTests
{
    [Fact]
    public void Normal_play_allocates_deterministic_unique_ids_including_the_in_flight_card()
    {
        var maker = Card(
            "maker",
            new CardEffect(
                "addCard",
                EffectScope.Self,
                null,
                2,
                CardRefId: "maker",
                Pile: "discard"));
        const string playingId = "maker-generated-0";
        var state = BattleFixtures.MinimalState(
            hand: [new BattleCardInstance(playingId, maker.Id, false, null)]);
        var catalog = BattleFixtures.MinimalCatalog(cards: [maker]);

        BattleState first = Play(state, catalog);
        BattleState second = Play(state, catalog);

        var firstGenerated = first.DiscardPile
            .Where(card => card.InstanceId != playingId)
            .Select(card => card.InstanceId)
            .ToArray();
        var secondGenerated = second.DiscardPile
            .Where(card => card.InstanceId != playingId)
            .Select(card => card.InstanceId)
            .ToArray();
        Assert.Equal(
            new[] { "maker-generated-1", "maker-generated-2" },
            firstGenerated);
        Assert.Equal(firstGenerated, secondGenerated);
        Assert.Equal(
            first.DiscardPile.Length,
            first.DiscardPile.Select(card => card.InstanceId).Distinct().Count());
        Assert.Equal(3, first.NextGeneratedCardOrdinal);
        Assert.Equal(first.NextGeneratedCardOrdinal, second.NextGeneratedCardOrdinal);
    }

    [Fact]
    public void Afterimage_reexecution_allocates_the_same_id_sequence_from_the_same_state()
    {
        var token = Card("token");
        var hero = BattleFixtures.WithStatus(
            BattleFixtures.Hero(), BattleStatusIds.Afterimage, 2);
        var snapshot = new PlayedCardEffectSnapshot(
            "reenact-source",
            "reenact-source-used",
            hero.InstanceId,
            CardType.Attack,
            OriginalCost: 1,
            PaidCost: 1,
            IsUpgraded: false,
            Keywords: [],
            Effects:
            [
                new CardEffect(
                    "addCard",
                    EffectScope.Self,
                    null,
                    1,
                    CardRefId: token.Id,
                    Pile: "discard"),
            ],
            PlaySequence: 1);
        var state = BattleFixtures.MinimalState(allies: [hero]) with
        {
            LastFinisherRecord = [snapshot],
        };
        var catalog = BattleFixtures.MinimalCatalog(cards: [token]);

        BattleState first = ResolveAfterimage(state, hero.InstanceId, catalog);
        BattleState second = ResolveAfterimage(state, hero.InstanceId, catalog);

        Assert.Equal(
            new[] { "token-generated-0", "token-generated-1" },
            first.DiscardPile.Select(card => card.InstanceId));
        Assert.Equal(
            first.DiscardPile.Select(card => card.InstanceId),
            second.DiscardPile.Select(card => card.InstanceId));
        Assert.Equal(2, first.NextGeneratedCardOrdinal);
        Assert.Equal(first.NextGeneratedCardOrdinal, second.NextGeneratedCardOrdinal);
    }

    [Fact]
    public void Contingency_execution_allocates_the_same_id_from_the_same_state()
    {
        var token = Card("token");
        var plan = Card(
            "plan",
            new CardEffect(
                "addCard",
                EffectScope.Self,
                null,
                1,
                CardRefId: token.Id,
                Pile: "discard"),
            abilities:
            [
                new ContingencyAbilityDefinition(
                    new ContingencyTriggerDefinition(
                        ContingencyTriggerTiming.TurnStart)),
            ]);
        var planInstance = new BattleCardInstance(
            "plan-used", plan.Id, false, null);
        var armed = new ArmedContingency(
            planInstance.InstanceId,
            "hero_inst",
            new ContingencyTriggerDefinition(
                ContingencyTriggerTiming.TurnStart),
            ArmedOrdinal: 1);
        var state = BattleFixtures.MinimalState() with
        {
            ExhaustPile = [planInstance],
            ArmedContingencies = [armed],
        };
        var catalog = BattleFixtures.MinimalCatalog(cards: [plan, token]);

        var first = ContingencyResolutionEngine.ResolveTurnStart(
            state, Rng(), catalog).State;
        var second = ContingencyResolutionEngine.ResolveTurnStart(
            state, Rng(), catalog).State;

        Assert.Equal(
            "token-generated-0",
            Assert.Single(first.DiscardPile, card =>
                card.CardDefinitionId == token.Id).InstanceId);
        Assert.Equal(
            Assert.Single(first.DiscardPile, card =>
                card.CardDefinitionId == token.Id).InstanceId,
            Assert.Single(second.DiscardPile, card =>
                card.CardDefinitionId == token.Id).InstanceId);
        Assert.Equal(1, first.NextGeneratedCardOrdinal);
        Assert.Equal(first.NextGeneratedCardOrdinal, second.NextGeneratedCardOrdinal);
    }

    private static BattleState Play(BattleState state, DataCatalog catalog)
    {
        var (next, _) = BattleEngine.PlayCard(
            state,
            handIndex: 0,
            targetEnemyIndex: 0,
            targetAllyIndex: 0,
            Rng(),
            catalog);
        return next;
    }

    private static BattleState ResolveAfterimage(
        BattleState state,
        string actorInstanceId,
        DataCatalog catalog)
    {
        var result = BattleEngine.ResolveAfterimageAtActorEnd(
            state, actorInstanceId, Rng(), catalog, orderStart: 0);
        Assert.False(result.Paused);
        return result.State;
    }

    private static CardDefinition Card(
        string id,
        CardEffect? effect = null,
        IReadOnlyList<CardAbilityDefinition>? abilities = null) =>
        new(
            id,
            id,
            null,
            CardRarity.Common,
            CardType.Skill,
            Cost: 0,
            UpgradedCost: null,
            Effects: effect is null ? [] : [effect],
            UpgradedEffects: null,
            Keywords: null,
            Abilities: abilities);

    private static IRng Rng() => new FakeRng(new int[32], []);
}
