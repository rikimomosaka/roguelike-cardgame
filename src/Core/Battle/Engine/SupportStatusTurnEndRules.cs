using System;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// Shared turn-boundary predicates for the support-class statuses. In
/// particular, Afterglow must protect both the legacy side countdown and the
/// custom Guardian/Combo countdowns while being consumed only once.
/// </summary>
internal static class SupportStatusTurnEndRules
{
    public static bool PreservesCountdownBuffs(CombatActor actor) =>
        actor.GetStatus(BattleStatusIds.Afterglow) > 0;

    public static bool ShouldSkipStandardCountdown(
        CombatActor actor,
        StatusDefinition definition) =>
        PreservesCountdownBuffs(actor)
        && definition.Kind == StatusKind.Buff
        && definition.TickDirection == StatusTickDirection.Decrement
        && !string.Equals(
            definition.Id,
            BattleStatusIds.Afterglow,
            StringComparison.Ordinal);

    /// <summary>
    /// Returns whether Afterglow protects at least one custom turn-boundary
    /// countdown. Standard status countdowns are tracked when they are
    /// actually skipped; inspecting them again here would incorrectly count a
    /// buff applied later during enemy actions or turn-end triggers.
    /// </summary>
    public static bool HasCustomBuffToPreserve(
        CombatActor actor,
        bool hasActiveCombo) =>
        actor.GetStatus(BattleStatusIds.Steadfast) > 0
        || actor.GetStatus(BattleStatusIds.Cover) > 0
        || actor.GetStatus(BattleStatusIds.DefensiveStance) > 0
        || hasActiveCombo
            && actor.GetStatus(BattleStatusIds.ComboCarryover) > 0;
}
