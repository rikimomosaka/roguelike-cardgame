using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// Owns reset versus carryover of participant combo fields. The explicit
/// pending marker survives until the first real card play of the next turn.
/// That card continues the numeric combo regardless of original cost, but it
/// does not receive combo cost reduction and no previous cost/Wild state is
/// carried across the boundary.
/// </summary>
internal static class ComboTurnBoundaryRules
{
    public static BattleState ResetAtTurnEnd(
        BattleState state,
        CombatActor? participantActor)
    {
        bool carry = participantActor is not null
            && participantActor.GetStatus(BattleStatusIds.ComboCarryover) > 0;
        return ResetTurnScopedState(
            state,
            preserveCombo: carry,
            comboCarryoverPending: carry);
    }

    public static BattleState ResetAtTurnStart(BattleState state)
    {
        bool carry = state.ComboCarryoverPending;
        return ResetTurnScopedState(
            state,
            preserveCombo: carry,
            comboCarryoverPending: state.ComboCarryoverPending);
    }

    public static BattleState ResetWithoutCarryover(BattleState state) =>
        ResetTurnScopedState(
            state,
            preserveCombo: false,
            comboCarryoverPending: false);

    private static BattleState ResetTurnScopedState(
        BattleState state,
        bool preserveCombo,
        bool comboCarryoverPending) => state with
    {
        ComboCount = preserveCombo ? state.ComboCount : 0,
        // A carried chain begins the new turn with its current count as the
        // live maximum. Historical maxima never leak into Momentum checks.
        MaxComboThisTurn = preserveCombo ? state.ComboCount : 0,
        // The opening card gets its one-time continuation from
        // ComboCarryoverPending, not from stale cost or Wild state. This also
        // keeps its cost ring and payment identical to an ordinary first card.
        LastPlayedOrigCost = null,
        NextCardComboFreePass = false,
        WildUsedInCurrentCombo = false,
        CurrentComboCardHistory = preserveCombo
            ? state.CurrentComboCardHistory
            : ImmutableArray<PlayedCardEffectSnapshot>.Empty,
        ComboCarryoverPending = comboCarryoverPending,
        PlayedCardHistoryThisTurn = ImmutableArray<PlayedCardEffectSnapshot>.Empty,
        LastFinisherRecord = ImmutableArray<PlayedCardEffectSnapshot>.Empty,
        UsedCharacterSkillIdsThisTurn = ImmutableHashSet<string>.Empty,
        CharacterSkillUsesThisTurn = ImmutableDictionary<string, int>.Empty,
        CardUsesThisTurn = ImmutableDictionary<string, int>.Empty,
        PotionUsesThisTurn = ImmutableDictionary<string, int>.Empty,
        CardUseLockedThisTurn = false,
    };
}
