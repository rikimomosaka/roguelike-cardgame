using System.Collections.Generic;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// 単一 CardEffect を BattleState に適用する。
/// Phase 10.2.A は "attack" / "block" のみ対応。その他 action は no-op。
/// 10.2.B〜E で対応 action を段階的に増やす。親 spec §5 参照。
/// </summary>
internal static class EffectApplier
{
    public static (BattleState, IReadOnlyList<BattleEvent>) Apply(
        BattleState state, CombatActor caster, CardEffect effect, IRng rng)
    {
        return effect.Action switch
        {
            "attack" => ApplyAttack(state, caster, effect),
            "block"  => ApplyBlock(state, caster, effect),
            _        => (state, System.Array.Empty<BattleEvent>()),
        };
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyAttack(
        BattleState state, CombatActor caster, CardEffect effect)
    {
        // stale ref 対策: state から InstanceId で最新の actor を再取得する
        var current = FetchActor(state, caster.InstanceId, caster.Side) ?? caster;
        var updated = effect.Scope switch
        {
            EffectScope.Single => current with { AttackSingle = current.AttackSingle.Add(effect.Amount) },
            EffectScope.Random => current with { AttackRandom = current.AttackRandom.Add(effect.Amount) },
            EffectScope.All    => current with { AttackAll    = current.AttackAll.Add(effect.Amount) },
            _ => current, // Self は CardEffect.Normalize で弾かれる想定
        };
        var next = ReplaceActor(state, caster.InstanceId, updated);
        return (next, System.Array.Empty<BattleEvent>());
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyBlock(
        BattleState state, CombatActor caster, CardEffect effect)
    {
        // 10.2.A は scope=Self のみ実装（敵の block も self、プレイヤーの defend も self）
        // scope=All / Random は 10.2.D で対応
        // stale ref 対策: state から InstanceId で最新の actor を再取得する
        var current = FetchActor(state, caster.InstanceId, caster.Side) ?? caster;
        var updated = current with { Block = current.Block.Add(effect.Amount) };
        var next = ReplaceActor(state, caster.InstanceId, updated);
        var ev = new BattleEvent(
            BattleEventKind.GainBlock, Order: 0,
            CasterInstanceId: caster.InstanceId,
            TargetInstanceId: caster.InstanceId,
            Amount: effect.Amount);
        return (next, new[] { ev });
    }

    /// <summary>
    /// state から InstanceId で最新の actor を取得する。
    /// caster が stale snapshot の場合でも正しい actor を返す。
    /// </summary>
    private static CombatActor? FetchActor(BattleState state, string instanceId, ActorSide side)
    {
        if (side == ActorSide.Ally)
        {
            foreach (var a in state.Allies)
                if (a.InstanceId == instanceId) return a;
        }
        else
        {
            foreach (var e in state.Enemies)
                if (e.InstanceId == instanceId) return e;
        }
        return null;
    }

    private static BattleState ReplaceActor(BattleState state, string instanceId, CombatActor after)
    {
        if (after.Side == ActorSide.Ally)
        {
            for (int i = 0; i < state.Allies.Length; i++)
            {
                if (state.Allies[i].InstanceId == instanceId)
                    return state with { Allies = state.Allies.SetItem(i, after) };
            }
        }
        else
        {
            for (int i = 0; i < state.Enemies.Length; i++)
            {
                if (state.Enemies[i].InstanceId == instanceId)
                    return state with { Enemies = state.Enemies.SetItem(i, after) };
            }
        }
        return state;
    }
}
