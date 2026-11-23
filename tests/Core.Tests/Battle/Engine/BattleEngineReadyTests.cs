using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public sealed class BattleEngineReadyTests
{
    [Fact]
    public void Ready_is_reversible_until_every_living_participant_is_ready()
    {
        var state = TwoHumanParticipants();
        var catalog = BattleFixtures.MinimalCatalog();
        var rng = new FakeRng([], []);
        string primaryId = state.Participants[0].ParticipantId;
        string allyId = state.Participants[1].ParticipantId;

        var (primaryReady, readyEvents) = BattleEngine.SetParticipantReady(
            state, primaryId, true, rng, catalog);

        Assert.Equal(1, primaryReady.Turn);
        Assert.Equal(BattlePhase.PlayerInput, primaryReady.Phase);
        Assert.True(primaryReady.Participants[0].IsReady);
        Assert.False(primaryReady.Participants[1].IsReady);
        Assert.DoesNotContain(
            readyEvents, battleEvent => battleEvent.Kind == BattleEventKind.EndTurn);

        var (primaryUnready, unreadyEvents) = BattleEngine.SetParticipantReady(
            primaryReady, primaryId, false, rng, catalog);

        Assert.False(primaryUnready.Participants[0].IsReady);
        Assert.Equal(1, primaryUnready.Turn);
        Assert.Equal(0, Assert.Single(unreadyEvents).Amount);

        var (readyAgain, _) = BattleEngine.SetParticipantReady(
            primaryUnready, primaryId, true, rng, catalog);
        var (nextTurn, resolutionEvents) = BattleEngine.SetParticipantReady(
            readyAgain, allyId, true, rng, catalog);

        Assert.Equal(2, nextTurn.Turn);
        Assert.Equal(BattlePhase.PlayerInput, nextTurn.Phase);
        Assert.All(nextTurn.Participants, participant => Assert.False(participant.IsReady));
        Assert.Contains(
            resolutionEvents,
            battleEvent => battleEvent.Kind == BattleEventKind.ResolutionStarted);
        Assert.Contains(
            resolutionEvents,
            battleEvent => battleEvent.Kind == BattleEventKind.EndTurn);
    }

    [Fact]
    public void Legacy_EndTurn_marks_primary_ready_without_resolving_while_human_ally_is_unready()
    {
        var state = TwoHumanParticipants();

        var (next, events) = BattleEngine.EndTurn(
            state, new FakeRng([], []), BattleFixtures.MinimalCatalog());

        Assert.Equal(1, next.Turn);
        Assert.Equal(BattlePhase.PlayerInput, next.Phase);
        Assert.True(next.Participants[0].IsReady);
        Assert.False(next.Participants[1].IsReady);
        Assert.Single(events);
        Assert.Equal(BattleEventKind.ParticipantReadyChanged, events[0].Kind);
    }

    [Fact]
    public void Ready_rejects_a_participant_with_an_unresolved_card_choice()
    {
        var state = TwoHumanParticipants();
        var primary = state.Participants[0] with
        {
            PendingCardPlay = new PendingCardPlay(
                "pending-card",
                0,
                SummonedActorInstanceIdsBefore: ImmutableArray<string>.Empty,
                new PendingChoice(
                    "discard",
                    "hand",
                    1,
                    ImmutableArray.Create("candidate"))),
        };
        state = BattleParticipantAccess.Replace(state, primary);

        Assert.Throws<System.InvalidOperationException>(() =>
            BattleEngine.SetParticipantReady(
                state,
                primary.ParticipantId,
                true,
                new FakeRng([], []),
                BattleFixtures.MinimalCatalog()));
    }

    [Fact]
    public void New_legal_card_action_unreadies_a_ready_human_without_rewriting_past_actions()
    {
        var state = TwoHumanParticipants();
        var strike = new BattleCardInstance(
            "ally-strike", "strike", false, null);
        var secondary = state.Participants[1] with
        {
            Energy = 0,
            Hand = ImmutableArray.Create(strike),
            IsReady = true,
        };
        state = BattleParticipantAccess.Replace(state, secondary);
        var afterEnergyGift = BattleParticipantAccess.Replace(
            state, secondary with { Energy = 1 });

        var result = BattleCpuPlanningCoordinator.PlanAfterCommittedAction(
            state,
            afterEnergyGift,
            state.Participants[0].ParticipantId,
            new FakeRng([], []),
            BattleFixtures.MinimalCatalog());

        Assert.Contains(secondary.ParticipantId, result.ReopenedParticipantIds);
        Assert.False(result.State.Participants.Single(
            participant => participant.ParticipantId == secondary.ParticipantId).IsReady);
        Assert.Empty(result.Actions);
    }

    [Fact]
    public void Numeric_combat_change_does_not_unready_a_human_with_same_legal_actions()
    {
        var state = TwoHumanParticipants();
        var secondary = state.Participants[1] with { IsReady = true };
        state = BattleParticipantAccess.Replace(state, secondary);
        var afterBlock = state with
        {
            Enemies = state.Enemies.SetItem(
                0,
                state.Enemies[0] with { Block = BlockPool.Empty.Add(5) }),
        };

        var result = BattleCpuPlanningCoordinator.PlanAfterCommittedAction(
            state,
            afterBlock,
            state.Participants[0].ParticipantId,
            new FakeRng([], []),
            BattleFixtures.MinimalCatalog());

        Assert.Empty(result.ReopenedParticipantIds);
        Assert.True(result.State.Participants.Single(
            participant => participant.ParticipantId == secondary.ParticipantId).IsReady);
    }

    [Fact]
    public void Ready_participant_must_unready_before_issuing_planning_commands()
    {
        var state = TwoHumanParticipants();
        var primary = state.Participants[0] with
        {
            Energy = 3,
            Hand = [new BattleCardInstance("ready-strike", "strike", false, null)],
            IsReady = true,
        };
        state = BattleParticipantAccess.Replace(state, primary);

        Assert.Throws<System.InvalidOperationException>(() =>
            BattleEngine.PlayCard(
                state, primary.ParticipantId, 0, 0, 0,
                new FakeRng([], []), BattleFixtures.MinimalCatalog()));
        Assert.Throws<System.InvalidOperationException>(() =>
            BattleEngine.SetTarget(
                state, primary.ParticipantId, ActorSide.Enemy, 0));
    }

    [Fact]
    public void Cpu_action_that_adds_a_legal_human_action_unreadies_that_human()
    {
        var unlock = new CardDefinition(
            "cpu-unlock", "cpu-unlock", null, CardRarity.Token, CardType.Skill,
            0, null,
            [new CardEffect(
                "debuff", EffectScope.All, EffectSide.Ally, -1,
                Name: BattleStatusIds.Bind)],
            null, null);
        var unit = new UnitDefinition(
            "unlock-unit", "unlock-unit", "unlock-unit", 30,
            InitialMoveId: "", Moves: [],
            Deck: [new UnitDeckEntry(unlock.Id)],
            InitialEnergy: 0, EnergyPerTurn: 1, DrawPerTurn: 1, HandLimit: 1,
            AiProfileId: UnitAiProfileIds.LowCostCombo);
        var hero = BattleFixtures.Hero() with
        {
            Statuses = ImmutableDictionary<string, int>.Empty
                .Add(BattleStatusIds.Bind, 1),
        };
        var cpuActor = BattleFixtures.SummonActor(
            "unlock-cpu", unit.Id, slotIndex: 1, hp: 30);
        var state = BattleFixtures.MinimalState(
            allies: [hero, cpuActor],
            hand: [new BattleCardInstance("human-strike", "strike", false, null)]);
        state = BattleParticipantAccess.InitializePrimary(state);
        state = BattleParticipantAccess.Replace(
            state,
            state.Participants[0] with { IsReady = true });
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [BattleFixtures.Strike(), unlock], units: [unit]);
        state = BattleCpuPlanningCoordinator.EnsureParticipants(
            state, new FakeRng([], []), catalog);
        var result = BattleCpuPlanningCoordinator.PlanAfterCommittedAction(
            state,
            state,
            state.Participants[0].ParticipantId,
            new FakeRng([], []),
            catalog);

        Assert.Single(result.Actions);
        Assert.Equal(0, result.State.Allies[0].GetStatus(BattleStatusIds.Bind));
        Assert.Contains(state.Participants[0].ParticipantId, result.ReopenedParticipantIds);
        Assert.False(result.State.Participants[0].IsReady);
    }

    private static BattleState TwoHumanParticipants()
    {
        var hero = BattleFixtures.Hero();
        var ally = BattleFixtures.SummonActor(
            "human-ally-instance", "future-human", slotIndex: 1, hp: 30);
        var state = BattleFixtures.MinimalState(
            allies: [hero, ally],
            draw: Enumerable.Range(0, 5)
                .Select(index => new BattleCardInstance(
                    $"draw-{index}", "strike", false, null))
                .ToImmutableArray());
        state = BattleParticipantAccess.InitializePrimary(state);
        var secondary = state.Participants[0] with
        {
            ParticipantId = "player:1",
            ActorInstanceId = ally.InstanceId,
            ControllerKind = BattleParticipantControllerKind.Human,
            OwnerPlayerSlot = 1,
            CharacterId = ally.DefinitionId,
            TargetAllyIndex = 1,
            Energy = 0,
            EnergyPerTurn = 1,
            DrawPerTurn = 0,
            DrawPile = [],
            Hand = [],
            DiscardPile = [],
            ExhaustPile = [],
            SummonHeld = [],
            PowerCards = [],
            OwnedRelicIds = [],
            Potions = [],
            PendingCardPlay = null,
            IsReady = false,
        };
        return BattleParticipantAccess.Add(state, secondary);
    }
}
