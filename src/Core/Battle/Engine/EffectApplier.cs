using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// 単一 CardEffect を BattleState に適用する。
/// Phase 10.2.B で buff / debuff action 追加（4 scope 対応）。
/// Phase 10.2.D で DataCatalog 引数を追加（upgrade / summon で使用予定）。
/// その他 action（heal/draw/discard/upgrade/exhaust*/retainSelf/gainEnergy/summon）は Tasks 5-11 で実装予定。
/// 親 spec §5 / Phase 10.2.D spec §3-1 参照。
/// </summary>
internal static class EffectApplier
{
    public static (BattleState, IReadOnlyList<BattleEvent>) Apply(
        BattleState state, CombatActor caster, CardEffect effect, IRng rng,
        DataCatalog catalog)                        // 10.2.D
    {
        return effect.Action switch
        {
            "attack" => ApplyAttack(state, caster, effect),
            "block"  => ApplyBlock(state, caster, effect),
            "buff"   => ApplyStatusChange(state, caster, effect, rng),
            "debuff" => ApplyStatusChange(state, caster, effect, rng),
            _        => (state, Array.Empty<BattleEvent>()),
        };
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyAttack(
        BattleState state, CombatActor caster, CardEffect effect)
    {
        // stale ref 対策: state から InstanceId で最新の actor を再取得する
        var current = FindActor(state, caster.InstanceId) ?? caster;
        var updated = effect.Scope switch
        {
            EffectScope.Single => current with { AttackSingle = current.AttackSingle.Add(effect.Amount) },
            EffectScope.Random => current with { AttackRandom = current.AttackRandom.Add(effect.Amount) },
            EffectScope.All    => current with { AttackAll    = current.AttackAll.Add(effect.Amount) },
            _ => current, // Self は CardEffect.Normalize で弾かれる想定
        };
        var next = ReplaceActor(state, caster.InstanceId, updated);
        return (next, Array.Empty<BattleEvent>());
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyBlock(
        BattleState state, CombatActor caster, CardEffect effect)
    {
        // 10.2.A は scope=Self のみ実装（敵の block も self、プレイヤーの defend も self）
        // scope=All / Random は 10.2.D で対応
        // stale ref 対策: state から InstanceId で最新の actor を再取得する
        var current = FindActor(state, caster.InstanceId) ?? caster;
        var updated = current with { Block = current.Block.Add(effect.Amount) };
        var next = ReplaceActor(state, caster.InstanceId, updated);
        var ev = new BattleEvent(
            BattleEventKind.GainBlock, Order: 0,
            CasterInstanceId: caster.InstanceId,
            TargetInstanceId: caster.InstanceId,
            Amount: effect.Amount);
        return (next, new[] { ev });
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyStatusChange(
        BattleState state, CombatActor caster, CardEffect effect, IRng rng)
    {
        if (string.IsNullOrEmpty(effect.Name))
            throw new InvalidOperationException($"buff/debuff effect requires Name (status id), got null/empty");

        var targets = ResolveTargets(state, caster, effect, rng);
        if (targets.Count == 0)
            return (state, Array.Empty<BattleEvent>());

        var events = new List<BattleEvent>();
        int order = 0;
        var s = state;

        // InstanceId snapshot で各 target を更新
        var targetIds = targets.Select(t => t.InstanceId).ToList();
        foreach (var tid in targetIds)
        {
            // 最新 state から再 fetch
            CombatActor? current = FindActor(s, tid);
            if (current is null) continue;

            int currentAmount = current.GetStatus(effect.Name);
            int newAmount = currentAmount + effect.Amount;

            ImmutableDictionary<string, int> newStatuses = newAmount <= 0
                ? current.Statuses.Remove(effect.Name)
                : current.Statuses.SetItem(effect.Name, newAmount);

            var updated = current with { Statuses = newStatuses };
            s = ReplaceActor(s, tid, updated);

            if (newAmount > 0 && currentAmount != newAmount)
            {
                events.Add(new BattleEvent(
                    BattleEventKind.ApplyStatus, Order: order++,
                    CasterInstanceId: caster.InstanceId,
                    TargetInstanceId: tid,
                    Amount: effect.Amount,
                    Note: effect.Name));
            }
            else if (newAmount <= 0 && currentAmount > 0)
            {
                events.Add(new BattleEvent(
                    BattleEventKind.RemoveStatus, Order: order++,
                    TargetInstanceId: tid,
                    Note: effect.Name));
            }
        }

        return (s, events);
    }

    private static IReadOnlyList<CombatActor> ResolveTargets(
        BattleState state, CombatActor caster, CardEffect effect, IRng rng)
    {
        switch (effect.Scope)
        {
            case EffectScope.Self:
                return new[] { caster };

            case EffectScope.Single:
                if (effect.Side is null)
                    throw new InvalidOperationException(
                        $"effect '{effect.Action}' Scope=Single requires non-null Side");
                if (effect.Side == EffectSide.Ally)
                    return state.TargetAllyIndex is { } ai && ai < state.Allies.Length
                        ? new[] { state.Allies[ai] }
                        : Array.Empty<CombatActor>();
                else
                    return state.TargetEnemyIndex is { } ei && ei < state.Enemies.Length
                        ? new[] { state.Enemies[ei] }
                        : Array.Empty<CombatActor>();

            case EffectScope.Random:
            {
                if (effect.Side is null)
                    throw new InvalidOperationException(
                        $"effect '{effect.Action}' Scope=Random requires non-null Side");
                var pool = (effect.Side == EffectSide.Ally ? state.Allies : state.Enemies)
                    .Where(a => a.IsAlive).ToList();
                if (pool.Count == 0) return Array.Empty<CombatActor>();
                int idx = rng.NextInt(0, pool.Count);
                return new[] { pool[idx] };
            }

            case EffectScope.All:
            {
                if (effect.Side is null)
                    throw new InvalidOperationException(
                        $"effect '{effect.Action}' Scope=All requires non-null Side");
                return (effect.Side == EffectSide.Ally ? state.Allies : state.Enemies)
                    .Where(a => a.IsAlive).ToList();
            }
        }
        return Array.Empty<CombatActor>();
    }

    /// <summary>
    /// state から InstanceId で最新の actor を取得する。
    /// caster が stale snapshot の場合でも正しい actor を返す。
    /// Ally / Enemy 両方を検索する。
    /// </summary>
    private static CombatActor? FindActor(BattleState state, string instanceId)
    {
        foreach (var a in state.Allies) if (a.InstanceId == instanceId) return a;
        foreach (var e in state.Enemies) if (e.InstanceId == instanceId) return e;
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
