using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// Resolves the Guardian-specific mitigation that is allowed only for enemy
/// Attack hits.  Cover spends Block but never substitutes HP or Barrier.
/// </summary>
internal static class GuardianCombatRules
{
    internal sealed record EnemyAttackResolution(
        BattleState State,
        CombatActor UpdatedTarget,
        IReadOnlyList<BattleEvent> Events,
        bool DiedNow,
        string? FullyBlockingActorInstanceId);

    public static EnemyAttackResolution ApplyEnemyAttack(
        BattleState state,
        CombatActor attacker,
        string targetInstanceId,
        int baseAmount,
        string scopeNote,
        int orderBase,
        int? hitIndex = null,
        HitContext? context = null)
    {
        int targetIndex = FindAllyIndex(state, targetInstanceId);
        if (targetIndex < 0)
            throw new System.InvalidOperationException(
                $"Enemy attack target '{targetInstanceId}' is not an allied actor.");

        var originalTarget = state.Allies[targetIndex];
        context ??= HitContext.Attack(CombatSource.Unattributed, hitIndex);
        int totalAttack = DealDamageHelper.CalculateTotalAttack(attacker, baseAmount);

        // The original target always pays first.
        var targetBlock = DealDamageHelper.ApplyBlock(originalTarget, totalAttack);
        var s = state with
        {
            Allies = state.Allies.SetItem(targetIndex, targetBlock.UpdatedActor),
        };
        int remaining = targetBlock.RemainingAttack;
        string? fullyBlockingActorId = targetBlock.Absorbed > 0 && remaining == 0
            ? originalTarget.InstanceId
            : null;
        var coverBlockEvents = new List<BattleEvent>();

        // Higher ally slots are closer to the enemy in the S1,S2,S3,P1..P4
        // formation.  Snapshot only identities/order; every actor is re-fetched
        // before paying so an earlier guard's live Block is authoritative.
        var guardIds = s.Allies
            .Where(actor => actor.IsAlive
                && actor.InstanceId != targetInstanceId
                && actor.GetStatus(BattleStatusIds.Cover) > 0)
            .OrderByDescending(actor => actor.SlotIndex)
            .ThenBy(actor => actor.InstanceId, System.StringComparer.Ordinal)
            .Select(actor => actor.InstanceId)
            .ToList();

        foreach (string guardId in guardIds)
        {
            if (remaining <= 0) break;

            int guardIndex = FindAllyIndex(s, guardId);
            if (guardIndex < 0) continue;
            var guard = s.Allies[guardIndex];
            if (!guard.IsAlive || guard.GetStatus(BattleStatusIds.Cover) <= 0)
                continue;

            var guarded = DealDamageHelper.ApplyBlock(guard, remaining);
            if (guarded.Absorbed <= 0) continue;

            s = s with { Allies = s.Allies.SetItem(guardIndex, guarded.UpdatedActor) };
            coverBlockEvents.Add(new BattleEvent(
                BattleEventKind.CoverBlockSpent,
                Order: 0,
                CasterInstanceId: attacker.InstanceId,
                TargetInstanceId: guard.InstanceId,
                Amount: guarded.Absorbed,
                Note: BattleStatusIds.Cover,
                HitIndex: hitIndex,
                DamageType: context.DamageType,
                Source: context.Source,
                CauseChain: context.CauseChain,
                HitLanded: totalAttack > 0));
            remaining = guarded.RemainingAttack;
            if (remaining == 0)
                fullyBlockingActorId = guardId;
        }

        // Re-fetch because a defensive helper must never overwrite a newer actor
        // value written while processing another Block provider.
        targetIndex = FindAllyIndex(s, targetInstanceId);
        var targetAfterAllBlock = s.Allies[targetIndex];
        var (updatedTarget, events, diedNow) = DealDamageHelper.ApplyAfterBlock(
            attacker,
            targetAfterAllBlock,
            totalAttack,
            remaining,
            targetBlock.Absorbed,
            scopeNote,
            orderBase,
            hitIndex,
            context);
        s = s with { Allies = s.Allies.SetItem(targetIndex, updatedTarget) };

        // Keep the AttackFire first, then attribute each Cover payment to its
        // actual provider before the target's DealDamage boundary. This lets
        // live and replay presentation reduce the correct Block pools.
        if (coverBlockEvents.Count > 0)
        {
            var attributed = new List<BattleEvent>(events.Count + coverBlockEvents.Count);
            foreach (var battleEvent in events)
            {
                if (battleEvent.Kind == BattleEventKind.DealDamage)
                {
                    foreach (var coverEvent in coverBlockEvents)
                        attributed.Add(coverEvent with
                        {
                            Order = orderBase + attributed.Count,
                        });
                }
                attributed.Add(battleEvent with
                {
                    Order = orderBase + attributed.Count,
                });
            }
            events = attributed;
        }

        return new EnemyAttackResolution(
            s, updatedTarget, events, diedNow, fullyBlockingActorId);
    }

    /// <summary>
    /// Grants DefensiveStance once for one actor in one logical hit.  Callers
    /// own the per-hit set so an All attack cannot reward the same Cover actor
    /// once per target.
    /// </summary>
    public static BattleState AwardDefensiveStance(
        BattleState state,
        string? actorInstanceId,
        ISet<string> rewardedActorsForLogicalHit,
        int? hitIndex,
        List<BattleEvent> events,
        ref int order)
    {
        if (actorInstanceId is null
            || !rewardedActorsForLogicalHit.Add(actorInstanceId))
            return state;

        int actorIndex = FindAllyIndex(state, actorInstanceId);
        if (actorIndex < 0) return state;
        var actor = state.Allies[actorIndex];
        if (!actor.IsAlive
            || actor.GetStatus(BattleStatusIds.DefensiveStance) <= 0
            || actor.GetStatus(BattleStatusIds.Stagnation) > 0)
            return state;

        int actualGain;
        int remainingGauge;
        BattleState updated;
        if (state.Participants.IsDefaultOrEmpty)
        {
            var legacyOwner = ProvisionalSoloHeroOwnerResolver.Resolve(state)?.Hero;
            if (legacyOwner?.InstanceId != actorInstanceId)
                return state;

            int before = System.Math.Clamp(
                state.ComboGauge, 0, BattleEngine.MaxComboGauge);
            remainingGauge = System.Math.Min(BattleEngine.MaxComboGauge, before + 1);
            actualGain = remainingGauge - before;
            updated = state with { ComboGauge = remainingGauge };
        }
        else
        {
            if (!BattleParticipantAccess.TryGetByActor(
                    state, actorInstanceId, out var participant))
                return state;

            int before = System.Math.Clamp(
                participant.ComboGauge, 0, BattleEngine.MaxComboGauge);
            remainingGauge = System.Math.Min(BattleEngine.MaxComboGauge, before + 1);
            actualGain = remainingGauge - before;
            updated = BattleParticipantAccess.Replace(
                state, participant with { ComboGauge = remainingGauge });
        }

        if (actualGain <= 0) return updated;

        var source = actor.GetStatusSource(BattleStatusIds.DefensiveStance)
            ?? new CombatSource(
                CombatSourceKind.Status,
                OwnerSlot: actor.OwnerPlayerSlot ?? actor.SlotIndex,
                OwnerInstanceId: actor.OwnerActorInstanceId ?? actor.InstanceId,
                ActorInstanceId: actor.InstanceId,
                DefinitionId: BattleStatusIds.DefensiveStance);
        events.Add(new BattleEvent(
            BattleEventKind.ComboGaugeChanged,
            Order: order++,
            CasterInstanceId: actor.InstanceId,
            TargetInstanceId: actor.InstanceId,
            Amount: actualGain,
            Note: $"remaining={remainingGauge}",
            HitIndex: hitIndex,
            Source: source));
        return updated;
    }

    private static int FindAllyIndex(BattleState state, string instanceId)
    {
        for (int i = 0; i < state.Allies.Length; i++)
            if (state.Allies[i].InstanceId == instanceId) return i;
        return -1;
    }
}
