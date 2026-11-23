using System;
using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// Applies actor statuses that truly expire at the shared battle turn
/// boundary. It runs after every participant has committed its card-resource
/// state so one actor's countdown cannot erase information needed to reset a
/// later participant.
/// </summary>
internal static class FormalTurnEndStatusProcessor
{
    public static (BattleState State, IReadOnlyList<BattleEvent> Events) Process(
        BattleState state,
        int orderStart = 0)
    {
        var actorIds = state.Allies.OrderBy(actor => actor.SlotIndex)
            .Select(actor => actor.InstanceId)
            .Concat(state.Enemies.OrderBy(actor => actor.SlotIndex)
                .Select(actor => actor.InstanceId))
            .ToArray();
        var events = new List<BattleEvent>();
        int order = orderStart;
        var s = state;

        foreach (string actorId in actorIds)
        {
            var actor = FindActor(s, actorId);
            if (actor is null)
                continue;

            bool hasActiveCombo = HasPendingComboCarryover(s, actorId);
            bool preserveBuffs =
                SupportStatusTurnEndRules.PreservesCountdownBuffs(actor);
            bool protectedStandardCountdown =
                s.AfterglowProtectedActorInstanceIds.Contains(actorId);
            bool protectsCustomCountdown = preserveBuffs
                && SupportStatusTurnEndRules.HasCustomBuffToPreserve(
                    actor,
                    hasActiveCombo);

            var guardian = GuardianTurnEndRules.ApplyCountdown(
                actor,
                preserveBuffs,
                order);
            actor = guardian.Actor;
            foreach (var battleEvent in guardian.Events)
            {
                events.Add(battleEvent with { Order = order++ });
            }

            if (!preserveBuffs
                && hasActiveCombo
                && actor.GetStatus(BattleStatusIds.ComboCarryover) > 0)
            {
                var statusSource = actor.GetStatusSource(
                    BattleStatusIds.ComboCarryover);
                bool removes = actor.GetStatus(
                    BattleStatusIds.ComboCarryover) == 1;
                actor = BattleStatusRules.Decrement(
                    actor,
                    BattleStatusIds.ComboCarryover);
                if (removes)
                {
                    events.Add(new BattleEvent(
                        BattleEventKind.RemoveStatus,
                        Order: order++,
                        TargetInstanceId: actorId,
                        Note: BattleStatusIds.ComboCarryover,
                        Source: statusSource));
                }
            }

            if (preserveBuffs
                && (protectedStandardCountdown || protectsCustomCountdown))
            {
                var statusSource = actor.GetStatusSource(BattleStatusIds.Afterglow);
                actor = BattleStatusRules.Clear(actor, BattleStatusIds.Afterglow);
                events.Add(new BattleEvent(
                    BattleEventKind.RemoveStatus,
                    Order: order++,
                    TargetInstanceId: actorId,
                    Note: BattleStatusIds.Afterglow,
                    Source: statusSource));
            }

            s = ReplaceActor(s, actorId, actor);
        }

        return (s with
        {
            AfterglowProtectedActorInstanceIds =
                System.Collections.Immutable.ImmutableHashSet<string>.Empty,
        }, events);
    }

    private static bool HasPendingComboCarryover(
        BattleState state,
        string actorInstanceId)
    {
        return BattleParticipantAccess.TryGetByActor(
                state,
                actorInstanceId,
                out var participant)
            && participant.ComboCarryoverPending;
    }

    private static CombatActor? FindActor(BattleState state, string instanceId)
    {
        foreach (var actor in state.Allies)
            if (string.Equals(actor.InstanceId, instanceId, StringComparison.Ordinal))
                return actor;
        foreach (var actor in state.Enemies)
            if (string.Equals(actor.InstanceId, instanceId, StringComparison.Ordinal))
                return actor;
        return null;
    }

    private static BattleState ReplaceActor(
        BattleState state,
        string instanceId,
        CombatActor replacement)
    {
        for (int i = 0; i < state.Allies.Length; i++)
        {
            if (string.Equals(
                    state.Allies[i].InstanceId,
                    instanceId,
                    StringComparison.Ordinal))
                return state with
                {
                    Allies = state.Allies.SetItem(i, replacement),
                };
        }

        for (int i = 0; i < state.Enemies.Length; i++)
        {
            if (string.Equals(
                    state.Enemies[i].InstanceId,
                    instanceId,
                    StringComparison.Ordinal))
                return state with
                {
                    Enemies = state.Enemies.SetItem(i, replacement),
                };
        }

        return state;
    }
}
