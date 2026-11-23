using System;
using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// Shared follow-up rules for a positive buff whose resolved target set
/// contains only its caster.
/// The original application is completed by the caller; this class copies the
/// buffs through Resonance without recursively treating those copies as new
/// self-buff applications.
/// </summary>
internal static class PositiveBuffApplicationRules
{
    /// <summary>
    /// Copies the positive buffs that the owner actually received to every
    /// other living actor on the same side, then consumes one Resonance.
    /// <paramref name="resonanceAmountBeforeApplication"/> snapshots the state
    /// before the grouped self-buff began, so gaining Resonance in that same
    /// group cannot make it trigger itself.
    /// </summary>
    public static BattleState ApplyResonance(
        BattleState state,
        string ownerInstanceId,
        int resonanceAmountBeforeApplication,
        IReadOnlyList<StatusChange> successfullyAppliedBuffs,
        CombatSource? source,
        List<BattleEvent> events,
        ref int order,
        DataCatalog? catalog = null,
        IRng? rng = null)
    {
        if (resonanceAmountBeforeApplication <= 0)
            return state;

        var changes = successfullyAppliedBuffs
            .Where(change => change.Amount > 0
                && !string.Equals(
                    change.Name,
                    BattleStatusIds.Resonance,
                    StringComparison.Ordinal)
                && StatusDefinition.Get(change.Name).Kind == StatusKind.Buff)
            .ToArray();
        if (changes.Length == 0)
            return state;

        var owner = FindActor(state, ownerInstanceId);
        if (owner is null)
            return state;

        string[] targetIds = (owner.Side == ActorSide.Ally
                ? state.Allies
                : state.Enemies)
            .Where(actor => actor.IsAlive
                && !string.Equals(
                    actor.InstanceId,
                    ownerInstanceId,
                    StringComparison.Ordinal))
            .OrderBy(actor => actor.SlotIndex)
            .Select(actor => actor.InstanceId)
            .ToArray();
        var resolvedSource = source ?? CombatSource.Unattributed;
        var s = state;

        foreach (string targetId in targetIds)
        {
            foreach (var change in changes)
            {
                var current = FindActor(s, targetId);
                if (current is null || !current.IsAlive)
                    break;

                int before = current.GetStatus(change.Name);
                var updated = string.Equals(
                        change.Name,
                        BattleStatusIds.Afterglow,
                        StringComparison.Ordinal)
                    ? current with
                    {
                        Statuses = current.Statuses.SetItem(
                            BattleStatusIds.Afterglow,
                            1),
                        StatusDisplayOrder = before == 0
                            ? BattleStatusRules.NormalizeStatusDisplayOrder(current)
                                .Remove(BattleStatusIds.Afterglow)
                                .Add(BattleStatusIds.Afterglow)
                            : BattleStatusRules.NormalizeStatusDisplayOrder(current),
                    }
                    : BattleStatusRules.AddSaturating(
                        current,
                        change.Name,
                        change.Amount);
                int after = updated.GetStatus(change.Name);
                if (before == after)
                    continue;

                updated = updated with
                {
                    StatusSources = updated.StatusSources.SetItem(
                        change.Name,
                        resolvedSource),
                };
                s = ReplaceActor(s, targetId, updated);
                events.Add(new BattleEvent(
                    BattleEventKind.ApplyStatus,
                    Order: order++,
                    CasterInstanceId: ownerInstanceId,
                    TargetInstanceId: targetId,
                    Amount: change.Amount,
                    Note: change.Name,
                    Source: resolvedSource));
            }
        }

        // Resonance is spent once an eligible self buff succeeded even when
        // there is no living recipient, or every recipient is already capped.
        var resonanceOwner = FindActor(s, ownerInstanceId);
        if (resonanceOwner is null
            || resonanceOwner.GetStatus(BattleStatusIds.Resonance) <= 0)
            return s;

        int amountBefore = resonanceOwner.GetStatus(BattleStatusIds.Resonance);
        var resonanceSource = resonanceOwner.GetStatusSource(
            BattleStatusIds.Resonance);
        var decremented = BattleStatusRules.Decrement(
            resonanceOwner,
            BattleStatusIds.Resonance);
        s = ReplaceActor(s, ownerInstanceId, decremented);
        events.Add(new BattleEvent(
            amountBefore == 1
                ? BattleEventKind.RemoveStatus
                : BattleEventKind.ApplyStatus,
            Order: order++,
            CasterInstanceId: ownerInstanceId,
            TargetInstanceId: ownerInstanceId,
            Amount: -1,
            Note: BattleStatusIds.Resonance,
            Source: resonanceSource));

        if (catalog is not null
            && rng is not null
            && BattleParticipantAccess.TryGetByActor(
                s, ownerInstanceId, out _))
        {
            var traitResult = CharacterTraitRuntime.TryTrigger(
                s,
                ownerInstanceId,
                CharacterTraitTrigger.OnResonanceConsumed,
                catalog,
                rng,
                orderStart: order);
            s = traitResult.State;
            foreach (var battleEvent in traitResult.Events)
                events.Add(battleEvent with { Order = order++ });
        }
        return s;
    }

    private static CombatActor? FindActor(
        BattleState state,
        string instanceId) =>
        state.Allies.FirstOrDefault(actor => actor.InstanceId == instanceId)
        ?? state.Enemies.FirstOrDefault(actor => actor.InstanceId == instanceId);

    private static BattleState ReplaceActor(
        BattleState state,
        string instanceId,
        CombatActor after)
    {
        for (int index = 0; index < state.Allies.Length; index++)
        {
            if (state.Allies[index].InstanceId == instanceId)
                return state with
                {
                    Allies = state.Allies.SetItem(index, after),
                };
        }

        for (int index = 0; index < state.Enemies.Length; index++)
        {
            if (state.Enemies[index].InstanceId == instanceId)
                return state with
                {
                    Enemies = state.Enemies.SetItem(index, after),
                };
        }

        return state;
    }
}
