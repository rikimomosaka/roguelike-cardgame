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
        var updated = effect.Scope switch
        {
            EffectScope.Single => caster with { AttackSingle = caster.AttackSingle.Add(effect.Amount) },
            EffectScope.Random => caster with { AttackRandom = caster.AttackRandom.Add(effect.Amount) },
            EffectScope.All    => caster with { AttackAll    = caster.AttackAll.Add(effect.Amount) },
            _ => caster, // Self は CardEffect.Normalize で弾かれる想定
        };
        var next = ReplaceActor(state, caster, updated);
        return (next, System.Array.Empty<BattleEvent>());
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyBlock(
        BattleState state, CombatActor caster, CardEffect effect)
    {
        // 10.2.A は scope=Self のみ実装（敵の block も self、プレイヤーの defend も self）
        // scope=All / Random は 10.2.D で対応
        var updated = caster with { Block = caster.Block.Add(effect.Amount) };
        var next = ReplaceActor(state, caster, updated);
        var ev = new BattleEvent(
            BattleEventKind.GainBlock, Order: 0,
            CasterInstanceId: caster.InstanceId,
            TargetInstanceId: caster.InstanceId,
            Amount: effect.Amount);
        return (next, new[] { ev });
    }

    private static BattleState ReplaceActor(BattleState state, CombatActor before, CombatActor after)
    {
        if (before.Side == ActorSide.Ally)
        {
            int idx = state.Allies.IndexOf(before);
            return state with { Allies = state.Allies.SetItem(idx, after) };
        }
        else
        {
            int idx = state.Enemies.IndexOf(before);
            return state with { Enemies = state.Enemies.SetItem(idx, after) };
        }
    }
}
