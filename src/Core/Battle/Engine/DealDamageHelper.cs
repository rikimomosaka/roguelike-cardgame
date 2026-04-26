using System.Collections.Generic;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// 1 体への DealDamage 計算ヘルパー。
/// 攻撃側 strength × addCount で遡及加算 → weak で 0.75 切捨 →
/// dexterity 反映の Block で吸収 → 残量に vulnerable で 1.5 倍 → HP 減算。
/// 親 spec §4-4 / Phase 10.2.B spec §3 参照。
/// </summary>
internal static class DealDamageHelper
{
    public static (CombatActor updatedTarget, IReadOnlyList<BattleEvent> events, bool diedNow) Apply(
        CombatActor attacker, CombatActor target,
        int baseSum, int addCount,
        string scopeNote, int orderBase)
    {
        // 1. 攻撃側補正
        int strength = attacker.GetStatus("strength");
        int weak     = attacker.GetStatus("weak");
        long boosted = (long)baseSum + (long)addCount * strength;
        int totalAttack = weak > 0 ? (int)(boosted * 3 / 4) : (int)boosted;

        // 2. Block 消費（敏捷遡及込み）
        int dex = target.GetStatus("dexterity");
        int preBlock = target.Block.Display(dex);
        int absorbed = System.Math.Min(totalAttack, preBlock);
        int rawDamage = totalAttack - absorbed;
        var newBlock = target.Block.Consume(totalAttack, dex);

        // 3. 受け側補正（脆弱）— Block 通り後に適用
        int vulnerable = target.GetStatus("vulnerable");
        int damage = vulnerable > 0 ? (rawDamage * 3) / 2 : rawDamage;

        // 4. HP 減算
        bool wasAlive = target.IsAlive;
        var updated = target with
        {
            Block = newBlock,
            CurrentHp = target.CurrentHp - damage,
        };
        bool diedNow = wasAlive && !updated.IsAlive;

        // 5. イベント発火
        var events = new List<BattleEvent>
        {
            new(BattleEventKind.AttackFire, Order: orderBase,
                CasterInstanceId: attacker.InstanceId, TargetInstanceId: target.InstanceId,
                Amount: totalAttack, Note: scopeNote),
            new(BattleEventKind.DealDamage, Order: orderBase + 1,
                CasterInstanceId: attacker.InstanceId, TargetInstanceId: target.InstanceId,
                Amount: damage, Note: scopeNote),
        };
        if (diedNow)
        {
            events.Add(new BattleEvent(
                BattleEventKind.ActorDeath, Order: orderBase + 2,
                CasterInstanceId: attacker.InstanceId, TargetInstanceId: target.InstanceId,
                Note: scopeNote));
        }

        return (updated, events, diedNow);
    }
}
