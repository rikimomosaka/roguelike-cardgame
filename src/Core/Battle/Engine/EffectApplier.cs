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
            "heal"   => ApplyHeal(state, caster, effect, rng),
            "draw"   => ApplyDraw(state, caster, effect, rng),
            "discard"=> ApplyDiscard(state, caster, effect, rng),
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

    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyHeal(
        BattleState state, CombatActor caster, CardEffect effect, IRng rng)
    {
        if (effect.Scope != EffectScope.Self && effect.Side != EffectSide.Ally)
            throw new InvalidOperationException(
                $"heal requires Side=Ally for scope {effect.Scope}, got {effect.Side}");

        // Self / Single / Random / All target 解決
        var targets = ResolveHealTargets(state, caster, effect, rng);
        if (targets.Count == 0) return (state, Array.Empty<BattleEvent>());

        var events = new List<BattleEvent>();
        int order = 0;
        var s = state;
        foreach (var target in targets)
        {
            var current = FindActor(s, target.InstanceId);
            if (current is null || !current.IsAlive) continue;
            int actualHeal = Math.Min(effect.Amount, current.MaxHp - current.CurrentHp);
            if (actualHeal <= 0) continue;  // already at MaxHp

            var updated = current with { CurrentHp = current.CurrentHp + actualHeal };
            s = ReplaceActor(s, target.InstanceId, updated);
            events.Add(new BattleEvent(
                BattleEventKind.Heal, Order: order++,
                CasterInstanceId: caster.InstanceId,
                TargetInstanceId: target.InstanceId,
                Amount: actualHeal));
        }
        return (s, events);
    }

    private static IReadOnlyList<CombatActor> ResolveHealTargets(
        BattleState state, CombatActor caster, CardEffect effect, IRng rng)
    {
        return effect.Scope switch
        {
            EffectScope.Self => new[] { caster },
            EffectScope.Single => state.TargetAllyIndex is { } ai && ai < state.Allies.Length
                ? new[] { state.Allies[ai] }
                : (IReadOnlyList<CombatActor>)Array.Empty<CombatActor>(),
            EffectScope.Random => PickRandomAlive(state.Allies, rng),
            EffectScope.All => state.Allies.Where(a => a.IsAlive).ToList(),
            _ => Array.Empty<CombatActor>(),
        };
    }

    private static IReadOnlyList<CombatActor> PickRandomAlive(
        ImmutableArray<CombatActor> pool, IRng rng)
    {
        var alive = pool.Where(a => a.IsAlive).ToList();
        if (alive.Count == 0) return Array.Empty<CombatActor>();
        int idx = rng.NextInt(0, alive.Count);
        return new[] { alive[idx] };
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyDraw(
        BattleState state, CombatActor caster, CardEffect effect, IRng rng)
    {
        if (effect.Scope != EffectScope.Self)
            throw new InvalidOperationException(
                $"draw requires Scope=Self, got {effect.Scope}");

        int requestedCount = effect.Amount;
        var hand = state.Hand.ToBuilder();
        var draw = state.DrawPile.ToBuilder();
        var discard = state.DiscardPile.ToBuilder();
        int actualDrawn = 0;
        const int handCap = 10;

        for (int i = 0; i < requestedCount; i++)
        {
            if (hand.Count >= handCap) break;
            if (draw.Count == 0)
            {
                if (discard.Count == 0) break;
                // Fisher-Yates shuffle discard → draw
                var arr = discard.ToArray();
                for (int j = arr.Length - 1; j > 0; j--)
                {
                    int k = rng.NextInt(0, j + 1);
                    (arr[j], arr[k]) = (arr[k], arr[j]);
                }
                foreach (var c in arr) draw.Add(c);
                discard.Clear();
            }
            var top = draw[0];
            draw.RemoveAt(0);
            hand.Add(top);
            actualDrawn++;
        }

        if (actualDrawn == 0) return (state, Array.Empty<BattleEvent>());

        var newState = state with
        {
            Hand = hand.ToImmutable(),
            DrawPile = draw.ToImmutable(),
            DiscardPile = discard.ToImmutable(),
        };
        var evs = new[] {
            new BattleEvent(BattleEventKind.Draw, Order: 0,
                CasterInstanceId: caster.InstanceId, Amount: actualDrawn),
        };
        return (newState, evs);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyDiscard(
        BattleState state, CombatActor caster, CardEffect effect, IRng rng)
    {
        if (effect.Scope == EffectScope.Single)
            throw new InvalidOperationException(
                "discard Scope=Single is not supported (UI not yet wired)");
        if (effect.Scope == EffectScope.Self)
            throw new InvalidOperationException(
                $"discard does not support Scope=Self");

        if (state.Hand.Length == 0) return (state, Array.Empty<BattleEvent>());

        string note;
        var hand = state.Hand.ToBuilder();
        var discard = state.DiscardPile.ToBuilder();

        if (effect.Scope == EffectScope.All)
        {
            note = "all";
            foreach (var c in hand) discard.Add(c);
            int discardedCount = hand.Count;
            hand.Clear();
            return BuildResult(state, caster, hand, discard, discardedCount, note);
        }
        else // Random
        {
            note = "random";
            int target = Math.Min(effect.Amount, hand.Count);
            for (int i = 0; i < target; i++)
            {
                int idx = rng.NextInt(0, hand.Count);
                var card = hand[idx];
                hand.RemoveAt(idx);
                discard.Add(card);
            }
            return BuildResult(state, caster, hand, discard, target, note);
        }
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) BuildResult(
        BattleState state, CombatActor caster,
        ImmutableArray<BattleCardInstance>.Builder hand,
        ImmutableArray<BattleCardInstance>.Builder discard,
        int discardedCount, string note)
    {
        var next = state with
        {
            Hand = hand.ToImmutable(),
            DiscardPile = discard.ToImmutable(),
        };
        if (discardedCount == 0) return (next, Array.Empty<BattleEvent>());
        var evs = new[] {
            new BattleEvent(BattleEventKind.Discard, Order: 0,
                CasterInstanceId: caster.InstanceId,
                Amount: discardedCount, Note: note),
        };
        return (next, evs);
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
