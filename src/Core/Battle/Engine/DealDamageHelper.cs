using System.Collections.Generic;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>Resolves one attack hit against one target.</summary>
internal static class DealDamageHelper
{
    internal readonly record struct AttackBlockResolution(
        CombatActor UpdatedActor,
        int Absorbed,
        int RemainingAttack);

    public static (CombatActor updatedTarget, IReadOnlyList<BattleEvent> events, bool diedNow) Apply(
        CombatActor attacker, CombatActor target,
        int baseAmount,
        string scopeNote, int orderBase,
        int? hitIndex = null,
        HitContext? context = null)
    {
        context ??= HitContext.Attack(CombatSource.Unattributed, hitIndex);

        int totalAttack = CalculateTotalAttack(attacker, baseAmount);
        var block = ApplyBlock(target, totalAttack);
        return ApplyAfterBlock(
            attacker,
            block.UpdatedActor,
            totalAttack,
            block.RemainingAttack,
            block.Absorbed,
            scopeNote,
            orderBase,
            hitIndex,
            context);
    }

    /// <summary>
    /// Applies attacker-side modifiers once.  Guardian mitigation uses the same
    /// adjusted amount before distributing it across the target and Cover actors.
    /// </summary>
    internal static int CalculateTotalAttack(CombatActor attacker, int baseAmount) =>
        AttackDamageMath.AdjustForAttacker(
            baseAmount,
            attacker.GetStatus("strength"),
            attacker.GetStatus("weak"),
            attacker.GetStatus("vigor"));

    /// <summary>Consumes ordinary Block before Vulnerable and Barrier.</summary>
    internal static AttackBlockResolution ApplyBlock(
        CombatActor actor, int incomingAttack)
    {
        int clampedAttack = System.Math.Max(0, incomingAttack);
        int dexterity = actor.GetStatus(BattleStatusIds.Dexterity);
        int available = actor.Block.Display(dexterity);
        int absorbed = System.Math.Min(clampedAttack, available);
        return new AttackBlockResolution(
            actor with { Block = actor.Block.Consume(clampedAttack, dexterity) },
            absorbed,
            clampedAttack - absorbed);
    }

    /// <summary>
    /// Finishes one hit after all Block providers have paid.  This boundary is
    /// shared by ordinary hits and Guardian Cover so Vulnerable, Barrier, revive,
    /// event attribution and death handling cannot drift between the two paths.
    /// blockedAmount is the target's own Block payment; Cover providers emit
    /// separate CoverBlockSpent events.
    /// </summary>
    internal static (CombatActor updatedTarget, IReadOnlyList<BattleEvent> events, bool diedNow)
        ApplyAfterBlock(
            CombatActor attacker,
            CombatActor targetAfterBlock,
            int totalAttack,
            int remainingAttack,
            int blockedAmount,
            string scopeNote,
            int orderBase,
            int? hitIndex,
            HitContext context)
    {
        var target = targetAfterBlock;
        int vulnerable = target.GetStatus("vulnerable");
        int rawDamage = System.Math.Max(0, remainingAttack);
        int damage = vulnerable > 0 ? (rawDamage * 3) / 2 : rawDamage;
        int hpBefore = System.Math.Max(0, target.CurrentHp);
        bool barrierConsumed = totalAttack > 0
            && target.GetStatus("barrier") > 0
            && damage > 0
            && hpBefore > 0;
        var statuses = target.Statuses;
        var statusSources = target.StatusSources;
        if (barrierConsumed)
        {
            int remaining = target.GetStatus("barrier") - 1;
            statuses = remaining == 0
                ? statuses.Remove("barrier")
                : statuses.SetItem("barrier", remaining);
            if (remaining == 0)
                statusSources = statusSources.Remove("barrier");
        }

        if (barrierConsumed)
            damage = 0;

        bool wasAlive = target.IsAlive;
        int actualHpLoss = System.Math.Min(hpBefore, damage);
        int overkill = System.Math.Max(0, damage - hpBefore);
        var updated = target with
        {
            CurrentHp = System.Math.Max(0, target.CurrentHp - damage),
            Statuses = statuses,
            StatusSources = statusSources,
        };
        bool revived = BattleStatusRules.TryRevive(updated, out var afterRevive);
        updated = afterRevive;
        bool diedNow = wasAlive && !updated.IsAlive;
        bool hitLanded = totalAttack > 0;

        var events = new List<BattleEvent>
        {
            new(BattleEventKind.AttackFire, Order: orderBase,
                CasterInstanceId: attacker.InstanceId, TargetInstanceId: target.InstanceId,
                Amount: totalAttack, Note: barrierConsumed ? $"{scopeNote};barrier" : scopeNote,
                HitIndex: hitIndex, DamageType: context.DamageType, Source: context.Source,
                CauseChain: context.CauseChain, HitLanded: hitLanded),
            new(BattleEventKind.DealDamage, Order: orderBase + 1,
                CasterInstanceId: attacker.InstanceId, TargetInstanceId: target.InstanceId,
                Amount: damage, Note: barrierConsumed ? $"{scopeNote};barrier" : scopeNote,
                BlockedAmount: System.Math.Max(0, blockedAmount), HitIndex: hitIndex,
                DamageType: context.DamageType, Source: context.Source,
                CauseChain: context.CauseChain,
                ActualHpLoss: actualHpLoss, Overkill: overkill, HitLanded: hitLanded),
        };
        if (revived)
        {
            events.Add(new BattleEvent(
                BattleEventKind.RemoveStatus, Order: orderBase + 2,
                TargetInstanceId: target.InstanceId,
                Note: BattleStatusIds.Revive,
                Source: target.GetStatusSource(BattleStatusIds.Revive)));
            events.Add(new BattleEvent(
                BattleEventKind.Heal, Order: orderBase + 3,
                TargetInstanceId: target.InstanceId,
                Amount: updated.CurrentHp,
                Note: BattleStatusIds.Revive));
        }
        if (diedNow)
        {
            events.Add(new BattleEvent(
                BattleEventKind.ActorDeath, Order: orderBase + 2,
                CasterInstanceId: attacker.InstanceId, TargetInstanceId: target.InstanceId,
                Note: scopeNote, HitIndex: hitIndex,
                DamageType: context.DamageType, Source: context.Source,
                CauseChain: context.CauseChain,
                ActualHpLoss: actualHpLoss, Overkill: overkill, HitLanded: hitLanded));
        }

        return (updated, events, diedNow);
    }
}
