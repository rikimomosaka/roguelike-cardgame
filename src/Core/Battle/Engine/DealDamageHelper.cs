using System.Collections.Generic;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// 1 体への DealDamage 計算ヘルパー。PlayerAttackingResolver / EnemyAttackingResolver から共有。
/// 親 spec §4-4 参照。10.2.B で力 / 脱力 / 脆弱の補正を本ヘルパー内に統合する。
/// </summary>
internal static class DealDamageHelper
{
    /// <summary>
    /// 1 回の攻撃を target に着弾させる。Block 消費 → HP 減算 → イベント発火。
    /// </summary>
    /// <returns>(更新後 target, イベント列, target が今この攻撃で死亡したか)</returns>
    public static (CombatActor updatedTarget, IReadOnlyList<BattleEvent> events, bool diedNow) Apply(
        CombatActor attacker, CombatActor target, int totalAttack, string scopeNote, int orderBase)
    {
        bool wasAlive = target.IsAlive;
        int preBlock = target.Block.RawTotal;
        int damage = System.Math.Max(0, totalAttack - preBlock);
        var newBlock = target.Block.Consume(totalAttack, dexterity: 0);
        var newHp = target.CurrentHp - damage;
        var updated = target with { Block = newBlock, CurrentHp = newHp };
        bool diedNow = wasAlive && !updated.IsAlive;

        var events = new List<BattleEvent>
        {
            new(BattleEventKind.AttackFire, Order: orderBase,
                CasterInstanceId: attacker.InstanceId,
                TargetInstanceId: target.InstanceId,
                Amount: totalAttack, Note: scopeNote),
            new(BattleEventKind.DealDamage, Order: orderBase + 1,
                CasterInstanceId: attacker.InstanceId,
                TargetInstanceId: target.InstanceId,
                Amount: damage, Note: scopeNote),
        };
        if (diedNow)
        {
            events.Add(new BattleEvent(
                BattleEventKind.ActorDeath, Order: orderBase + 2,
                CasterInstanceId: attacker.InstanceId,
                TargetInstanceId: target.InstanceId,
                Note: scopeNote));
        }
        return (updated, events, diedNow);
    }
}
