using System.Collections.Generic;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>Resolves one attack hit against one target.</summary>
internal static class DealDamageHelper
{
    public static (CombatActor updatedTarget, IReadOnlyList<BattleEvent> events, bool diedNow) Apply(
        CombatActor attacker, CombatActor target,
        int baseAmount,
        string scopeNote, int orderBase,
        int? hitIndex = null,
        HitContext? context = null)
    {
        context ??= HitContext.Attack(CombatSource.Unattributed, hitIndex);

        int totalAttack = AttackDamageMath.AdjustForAttacker(
            baseAmount,
            attacker.GetStatus("strength"),
            attacker.GetStatus("weak"));

        // Barrier is checked before Block. A positive attack consumes exactly one
        // stack, still counts as a landed hit, and leaves Block untouched.
        bool barrierConsumed = totalAttack > 0 && target.GetStatus("barrier") > 0;
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

        int dex = target.GetStatus("dexterity");
        int preBlock = target.Block.Display(dex);
        int absorbed = barrierConsumed ? 0 : System.Math.Min(totalAttack, preBlock);
        int rawDamage = barrierConsumed ? 0 : totalAttack - absorbed;
        var newBlock = barrierConsumed ? target.Block : target.Block.Consume(totalAttack, dex);

        int vulnerable = target.GetStatus("vulnerable");
        int damage = vulnerable > 0 ? (rawDamage * 3) / 2 : rawDamage;

        bool wasAlive = target.IsAlive;
        int hpBefore = System.Math.Max(0, target.CurrentHp);
        int actualHpLoss = System.Math.Min(hpBefore, damage);
        int overkill = System.Math.Max(0, damage - hpBefore);
        var updated = target with
        {
            Block = newBlock,
            CurrentHp = System.Math.Max(0, target.CurrentHp - damage),
            Statuses = statuses,
            StatusSources = statusSources,
        };
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
                BlockedAmount: absorbed, HitIndex: hitIndex,
                DamageType: context.DamageType, Source: context.Source,
                CauseChain: context.CauseChain,
                ActualHpLoss: actualHpLoss, Overkill: overkill, HitLanded: hitLanded),
        };
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
