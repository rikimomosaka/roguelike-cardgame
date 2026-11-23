using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public sealed class BattleEngineReadyDetailedTests
{
    [Fact]
    public void Detailed_ready_keeps_current_and_next_turn_cpu_actions_out_of_resolution_events()
    {
        var (state, catalog) = CpuAllyBattle();

        var result = BattleEngine.SetParticipantReadyDetailed(
            state,
            BattleParticipantAccess.DefaultPrimaryParticipantId,
            isReady: true,
            new MinimumRng(),
            catalog);

        Assert.True(result.DidResolve);
        Assert.Equal(2, result.State.Turn);
        Assert.Equal(1, result.StateAfterReady.Turn);
        Assert.Equal(BattlePhase.PlayerInput, result.StateAfterReady.Phase);
        Assert.Equal(100, result.StateAfterReady.Enemies[0].CurrentHp);
        Assert.Equal(1, result.StateAfterReady.Allies[1].AttackSingle.AddCount);
        Assert.Equal(2, result.StateAfterResolutionBeforeNextTurnPlanning.Turn);
        Assert.Equal(BattlePhase.PlayerInput,
            result.StateAfterResolutionBeforeNextTurnPlanning.Phase);
        Assert.Equal(0,
            result.StateAfterResolutionBeforeNextTurnPlanning
                .Allies[1].AttackSingle.AddCount);
        Assert.Equal(1, result.State.Allies[1].AttackSingle.AddCount);
        Assert.Equal(
            BattleEventKind.ParticipantReadyChanged,
            Assert.Single(result.ReadyEvents).Kind);
        Assert.Equal(
            BattleEventKind.ResolutionStarted,
            result.ResolutionEvents[0].Kind);
        Assert.Contains(
            result.ResolutionEvents,
            battleEvent => battleEvent.Kind == BattleEventKind.EndTurn);
        Assert.DoesNotContain(
            result.ResolutionEvents,
            battleEvent => battleEvent.Kind == BattleEventKind.PlayCard);

        var beforeResolution = Assert.Single(
            result.CpuActionsBeforeResolution);
        var afterResolution = Assert.Single(
            result.CpuActionsAfterResolution);
        Assert.Equal(1, beforeResolution.Turn);
        Assert.Equal(2, afterResolution.Turn);
        Assert.NotNull(beforeResolution.CardSnapshot);
        Assert.NotNull(afterResolution.CardSnapshot);
        Assert.Equal(2, result.CpuActions.Length);
        Assert.Same(
            result.StateAfterResolutionBeforeNextTurnPlanning,
            afterResolution.StateBefore);

        int firstPlay = result.Events
            .Select((battleEvent, index) => (battleEvent, index))
            .First(item => item.battleEvent.Kind == BattleEventKind.PlayCard)
            .index;
        int resolutionStarted = result.Events
            .Select((battleEvent, index) => (battleEvent, index))
            .First(item => item.battleEvent.Kind == BattleEventKind.ResolutionStarted)
            .index;
        int readyChanged = result.Events
            .Select((battleEvent, index) => (battleEvent, index))
            .First(item => item.battleEvent.Kind == BattleEventKind.ParticipantReadyChanged)
            .index;
        int lastPlay = result.Events
            .Select((battleEvent, index) => (battleEvent, index))
            .Last(item => item.battleEvent.Kind == BattleEventKind.PlayCard)
            .index;
        Assert.True(firstPlay < readyChanged);
        Assert.True(readyChanged < resolutionStarted);
        Assert.True(lastPlay > resolutionStarted);
        Assert.Equal(
            Enumerable.Range(0, result.Events.Length),
            result.Events.Select(battleEvent => battleEvent.Order));
    }

    [Fact]
    public void Legacy_ready_api_flattens_the_detailed_segments_without_losing_events()
    {
        var (detailedState, catalog) = CpuAllyBattle();
        var (legacyState, _) = CpuAllyBattle();

        var detailed = BattleEngine.SetParticipantReadyDetailed(
            detailedState,
            BattleParticipantAccess.DefaultPrimaryParticipantId,
            isReady: true,
            new MinimumRng(),
            catalog);
        var (legacyNext, legacyEvents) = BattleEngine.SetParticipantReady(
            legacyState,
            BattleParticipantAccess.DefaultPrimaryParticipantId,
            isReady: true,
            new MinimumRng(),
            catalog);

        Assert.Equal(detailed.State.Turn, legacyNext.Turn);
        Assert.Equal(detailed.State.Phase, legacyNext.Phase);
        Assert.Equal(detailed.State.ActionRevision, legacyNext.ActionRevision);
        Assert.Equal(
            detailed.State.Enemies.Select(enemy => enemy.CurrentHp),
            legacyNext.Enemies.Select(enemy => enemy.CurrentHp));
        Assert.Equal(
            detailed.State.Participants.Select(participant =>
                (participant.ParticipantId, participant.IsReady,
                 participant.Energy, participant.Hand.Length,
                 participant.DiscardPile.Length)),
            legacyNext.Participants.Select(participant =>
                (participant.ParticipantId, participant.IsReady,
                 participant.Energy, participant.Hand.Length,
                 participant.DiscardPile.Length)));
        Assert.Equal(detailed.LegacyEvents.ToArray(), legacyEvents.ToArray());
        Assert.Equal(
            2,
            legacyEvents.Count(battleEvent =>
                battleEvent.Kind == BattleEventKind.PlayCard));
    }

    [Fact]
    public void Detailed_unready_has_only_the_ready_transition_and_no_planning()
    {
        var (state, catalog) = CpuAllyBattle();

        var result = BattleEngine.SetParticipantReadyDetailed(
            state,
            BattleParticipantAccess.DefaultPrimaryParticipantId,
            isReady: false,
            new MinimumRng(),
            catalog);

        Assert.False(result.DidResolve);
        Assert.Single(result.ReadyEvents);
        Assert.Empty(result.ResolutionEvents);
        Assert.Empty(result.CpuActions);
        Assert.Null(result.ReadyCpuPlanning);
        Assert.Null(result.NextTurnCpuPlanning);
        Assert.Equal(result.ReadyEvents.ToArray(), result.Events.ToArray());
    }

    private static (BattleState State, RoguelikeCardGame.Core.Data.DataCatalog Catalog)
        CpuAllyBattle()
    {
        var card = new CardDefinition(
            "cpu-free-hit", "cpu-free-hit", null,
            CardRarity.Token, CardType.Attack,
            Cost: 0, UpgradedCost: null,
            Effects:
            [
                new CardEffect(
                    "attack", EffectScope.Single, EffectSide.Enemy, 1),
            ],
            UpgradedEffects: null,
            Keywords: null);
        var unit = new UnitDefinition(
            "cpu-loop-unit", "cpu-loop-unit", "cpu-loop-unit", 30,
            InitialMoveId: "",
            Moves: [],
            Deck: [new UnitDeckEntry(card.Id)],
            InitialEnergy: 0,
            EnergyPerTurn: 0,
            DrawPerTurn: 1,
            HandLimit: 1,
            AiProfileId: UnitAiProfileIds.LowCostCombo);
        var hero = BattleFixtures.Hero();
        var cpuActor = BattleFixtures.SummonActor(
            "cpu-loop-instance", unit.Id, slotIndex: 1, hp: 30) with
        {
            OwnerPlayerSlot = 0,
            OwnerActorInstanceId = hero.InstanceId,
        };
        var enemy = BattleFixtures.Goblin(slotIndex: 0, hp: 100);
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero, cpuActor),
            enemies: ImmutableArray.Create(enemy));
        state = BattleParticipantAccess.InitializePrimary(state);
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [card],
            units: [unit]);
        return (state, catalog);
    }

    private sealed class MinimumRng : IRng
    {
        public int NextInt(int minInclusive, int maxExclusive) => minInclusive;
        public double NextDouble() => 0;
    }
}
