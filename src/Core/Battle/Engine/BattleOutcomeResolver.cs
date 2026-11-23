using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// Single authoritative checkpoint for battle completion.  Defeat is checked
/// before victory so a mutual knockout can never be reported as a win.
/// </summary>
internal static class BattleOutcomeResolver
{
    public static RoguelikeCardGame.Core.Battle.State.BattleOutcome Evaluate(
        BattleState state)
    {
        if (state.Outcome != RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending)
            return state.Outcome;

        if (!IsRequiredPlayerAlive(state))
            return RoguelikeCardGame.Core.Battle.State.BattleOutcome.Defeat;
        if (!state.Enemies.Any(enemy => enemy.IsAlive))
            return RoguelikeCardGame.Core.Battle.State.BattleOutcome.Victory;
        return RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending;
    }

    public static (BattleState State, IReadOnlyList<BattleEvent> Events)
        ApplyIfTerminal(BattleState state, int orderStart = 0)
    {
        var outcome = Evaluate(state);
        if (outcome == RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending
            || state.Outcome != RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending)
            return (state, ImmutableArray<BattleEvent>.Empty);

        var participants = state.Participants.IsDefault
            ? ImmutableArray<BattleParticipantState>.Empty
            : state.Participants
                .Select(participant => participant with
                {
                    PendingCardPlay = null,
                    IsReady = true,
                })
                .ToImmutableArray();
        var terminal = state with
        {
            Phase = BattlePhase.Resolved,
            Outcome = outcome,
            ComboCount = 0,
            MaxComboThisTurn = 0,
            LastPlayedOrigCost = null,
            NextCardComboFreePass = false,
            WildUsedInCurrentCombo = false,
            UsedCharacterSkillIdsThisTurn = ImmutableHashSet<string>.Empty,
            CharacterSkillUsesThisTurn = ImmutableDictionary<string, int>.Empty,
            CardUseLockedThisTurn = false,
            CardUsesThisTurn = ImmutableDictionary<string, int>.Empty,
            PotionUsesThisTurn = ImmutableDictionary<string, int>.Empty,
            PendingCardPlay = null,
            Participants = participants,
        };
        terminal = BattleCardCostModifierLifecycle.Remove(
            terminal, CardCostModifierDuration.UntilTurnEnd);
        terminal = terminal with
        {
            Allies = terminal.Allies.Select(actor => actor with
            {
                Statuses = actor.Statuses
                    .Remove(BattleCardCostCalculator.HandCostReductionStatusId)
                    .Remove(BattleCardCostCalculator.HandCostIncreaseStatusId),
                StatusSources = actor.StatusSources
                    .Remove(BattleCardCostCalculator.HandCostReductionStatusId)
                    .Remove(BattleCardCostCalculator.HandCostIncreaseStatusId),
            }).ToImmutableArray(),
        };
        terminal = BattleParticipantTurnLifecycle.ApplyTerminalReset(terminal);

        return (
            terminal,
            ImmutableArray.Create(new BattleEvent(
                BattleEventKind.BattleEnd,
                Order: orderStart,
                Note: outcome.ToString())));
    }

    private static bool IsRequiredPlayerAlive(BattleState state)
    {
        if (!state.Participants.IsDefaultOrEmpty)
        {
            string actorInstanceId = state.Participants[0].ActorInstanceId;
            return state.Allies.Any(actor => actor.IsAlive
                && string.Equals(
                    actor.InstanceId, actorInstanceId, System.StringComparison.Ordinal));
        }

        return state.Allies.Any(actor =>
            actor.DefinitionId == "hero" && actor.IsAlive);
    }
}
