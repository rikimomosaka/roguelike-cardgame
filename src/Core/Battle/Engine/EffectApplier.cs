using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;   // 10.2.D: UnitDefinition
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
        // 10.5.D: AmountSource を runtime 値で resolve。null 指定の effect は素通し。
        var resolved = ResolveAmount(effect, state, caster);

        return resolved.Action switch
        {
            "attack" => ApplyAttack(state, caster, resolved),
            "block"  => ApplyBlock(state, caster, resolved),
            "buff"   => ApplyStatusChange(state, caster, resolved, rng),
            "debuff" => ApplyStatusChange(state, caster, resolved, rng),
            "heal"   => ApplyHeal(state, caster, resolved, rng),
            "draw"   => ApplyDraw(state, caster, resolved, rng),
            // Why: 既存 reward_*.json の draw 効果カードは "drawCards" 記法で
            // 書かれており、potion 側 "draw" と表記揺れしていた。両方サポート
            // することで洞察 / 叡智の奔流 / 戦術的撤退 等の効果が反映される。
            "drawCards" => ApplyDraw(state, caster, resolved, rng),
            "discard"=> ApplyDiscard(state, caster, resolved, rng, catalog),
            "exhaustSelf" => ApplyExhaustSelf(state, caster, rng, catalog),
            "retainSelf"  => (state, Array.Empty<BattleEvent>()),
            "gainEnergy"  => ApplyGainEnergy(state, caster, resolved),
            "exhaustCard" => ApplyExhaustCard(state, caster, resolved, rng, catalog),
            "upgrade"     => ApplyUpgrade(state, caster, resolved, rng, catalog),
            "summon"      => ApplySummon(state, caster, resolved, rng, catalog),
            // Phase 10.5.F: engine 新 actions
            "selfDamage"        => ApplySelfDamage(state, caster, resolved, rng, catalog),
            "addCard"           => ApplyAddCard(state, caster, resolved),
            "recoverFromDiscard"=> ApplyRecoverFromDiscard(state, caster, resolved, rng),
            "gainMaxEnergy"     => ApplyGainMaxEnergy(state, caster, resolved),
            _        => (state, Array.Empty<BattleEvent>()),
        };
    }

    /// <summary>
    /// 10.5.D: effect.AmountSource が non-null なら AmountSourceEvaluator で
    /// runtime 値を計算し、新 Amount を持った effect を返す。null は素通し。
    /// </summary>
    private static CardEffect ResolveAmount(
        CardEffect effect, BattleState state, CombatActor caster)
    {
        if (string.IsNullOrEmpty(effect.AmountSource)) return effect;
        int evaluated = AmountSourceEvaluator.Evaluate(effect.AmountSource!, state, caster);
        return effect with { Amount = evaluated };
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyExhaustSelf(
        BattleState state, CombatActor caster, IRng rng, DataCatalog catalog)
    {
        var ev = new BattleEvent(
            BattleEventKind.Exhaust, Order: 0,
            CasterInstanceId: caster.InstanceId,
            Amount: 1, Note: "self");
        var events = new List<BattleEvent> { ev };
        // Phase 10.5.L1.5: OnCardExhausted relic / power 発火
        return FireOnCardExhausted(state, events, rng, catalog);
    }

    /// <summary>
    /// Phase 10.5.L1.5: discard/exhaust の末尾で OnCardDiscarded / OnCardExhausted の
    /// Relic + Power を fire する共通ヘルパ。base events に新規 events を append し、
    /// state を更新して返す。
    /// </summary>
    private static (BattleState, IReadOnlyList<BattleEvent>) FireOnCardDiscarded(
        BattleState state, List<BattleEvent> baseEvents, IRng rng, DataCatalog catalog)
        => FireTrigger(state, baseEvents, "OnCardDiscarded", rng, catalog);

    private static (BattleState, IReadOnlyList<BattleEvent>) FireOnCardExhausted(
        BattleState state, List<BattleEvent> baseEvents, IRng rng, DataCatalog catalog)
        => FireTrigger(state, baseEvents, "OnCardExhausted", rng, catalog);

    private static (BattleState, IReadOnlyList<BattleEvent>) FireTrigger(
        BattleState state, List<BattleEvent> baseEvents, string trigger,
        IRng rng, DataCatalog catalog)
    {
        var s = state;
        var (afterRelic, evsRelic) = RelicTriggerProcessor.Fire(
            s, trigger, catalog, rng, orderStart: baseEvents.Count);
        s = afterRelic;
        baseEvents.AddRange(evsRelic);
        var (afterPower, evsPower) = PowerTriggerProcessor.Fire(
            s, trigger, catalog, rng, orderStart: baseEvents.Count);
        s = afterPower;
        baseEvents.AddRange(evsPower);
        return (s, baseEvents);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyGainEnergy(
        BattleState state, CombatActor caster, CardEffect effect)
    {
        if (effect.Scope != EffectScope.Self)
            throw new InvalidOperationException(
                $"gainEnergy requires Scope=Self, got {effect.Scope}");
        var next = state with { Energy = state.Energy + effect.Amount };
        var ev = new BattleEvent(
            BattleEventKind.GainEnergy, Order: 0,
            CasterInstanceId: caster.InstanceId,
            Amount: effect.Amount);
        return (next, new[] { ev });
    }

    // ========== Phase 10.5.F handlers ==========

    /// <summary>
    /// 10.5.F: caster の HP を block 無視で直接削る (Lose HP)。
    /// 死亡したら ActorDeath を emit。Outcome 確定は呼出側に任せる。
    /// 10.5.E: hero への self-damage 後 OnDamageReceived power が発火する。
    /// </summary>
    private static (BattleState, IReadOnlyList<BattleEvent>) ApplySelfDamage(
        BattleState state, CombatActor caster, CardEffect effect,
        IRng rng, DataCatalog catalog)
    {
        if (effect.Scope != EffectScope.Self)
            throw new InvalidOperationException(
                $"selfDamage requires Scope=Self, got {effect.Scope}");

        var current = FindActor(state, caster.InstanceId) ?? caster;
        int newHp = Math.Max(0, current.CurrentHp - effect.Amount);
        var updated = current with { CurrentHp = newHp };
        var next = ReplaceActor(state, caster.InstanceId, updated);

        var events = new List<BattleEvent>
        {
            new(BattleEventKind.DealDamage, Order: 0,
                CasterInstanceId: caster.InstanceId,
                TargetInstanceId: caster.InstanceId,
                Amount: effect.Amount,
                Note: "selfDamage"),
        };
        int order = 1;
        if (current.IsAlive && !updated.IsAlive)
        {
            events.Add(new BattleEvent(
                BattleEventKind.ActorDeath, Order: order++,
                TargetInstanceId: caster.InstanceId,
                Note: "selfDamage"));
        }
        else if (updated.DefinitionId == "hero" && updated.IsAlive && effect.Amount > 0)
        {
            // 10.5.E: hero に damage が入った直後 OnDamageReceived power fire
            var (afterPower, evsPower) = PowerTriggerProcessor.FireOnDamageReceived(
                next, catalog, rng, orderStart: order);
            next = afterPower;
            foreach (var ev in evsPower) { events.Add(ev with { Order = order++ }); }
        }

        return (next, events);
    }

    /// <summary>
    /// 10.5.F: 新規 BattleCardInstance を生成し pile (hand/draw/discard/exhaust) に追加。
    /// hand 上限 (10) 超過分は discard に流す。CardRefId / Pile は必須。
    /// </summary>
    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyAddCard(
        BattleState state, CombatActor caster, CardEffect effect)
    {
        if (string.IsNullOrEmpty(effect.CardRefId))
            throw new InvalidOperationException(
                "addCard requires non-null CardRefId");
        if (string.IsNullOrEmpty(effect.Pile))
            throw new InvalidOperationException(
                "addCard requires non-null Pile (hand/draw/discard/exhaust)");
        if (effect.Amount <= 0) return (state, Array.Empty<BattleEvent>());

        var s = state;
        int added = 0;
        for (int i = 0; i < effect.Amount; i++)
        {
            var instance = new BattleCardInstance(
                InstanceId: NewBattleInstanceId(effect.CardRefId!),
                CardDefinitionId: effect.CardRefId!,
                IsUpgraded: false,
                CostOverride: null);
            s = AddInstanceToPile(s, instance, effect.Pile!);
            added++;
        }

        var ev = new BattleEvent(
            BattleEventKind.AddCard, Order: 0,
            CasterInstanceId: caster.InstanceId,
            Amount: added,
            Note: $"{effect.CardRefId}:{effect.Pile}");
        return (s, new[] { ev });
    }

    /// <summary>
    /// 10.5.F: discard pile から N 枚を hand or exhaust に移動。
    /// Select=all/random 対応。choose は NotImplementedException (10.5.M で実装)。
    /// hand overflow は discard に戻す。
    /// </summary>
    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyRecoverFromDiscard(
        BattleState state, CombatActor caster, CardEffect effect, IRng rng)
    {
        if (effect.Pile != "hand" && effect.Pile != "exhaust")
            throw new InvalidOperationException(
                $"recoverFromDiscard requires Pile='hand' or 'exhaust', got '{effect.Pile}'");

        // M6.9: choose は UI 入力フロー未実装のため random fallback。
        var select = effect.Select ?? "random";

        if (state.DiscardPile.Length == 0)
            return (state, Array.Empty<BattleEvent>());

        var discardBuilder = state.DiscardPile.ToBuilder();
        var picked = new List<BattleCardInstance>();

        if (select == "all")
        {
            picked.AddRange(discardBuilder);
            discardBuilder.Clear();
        }
        else // "random" (default)
        {
            int target = Math.Min(effect.Amount, discardBuilder.Count);
            for (int i = 0; i < target; i++)
            {
                int idx = rng.NextInt(0, discardBuilder.Count);
                picked.Add(discardBuilder[idx]);
                discardBuilder.RemoveAt(idx);
            }
        }

        if (picked.Count == 0)
            return (state, Array.Empty<BattleEvent>());

        var s = state with { DiscardPile = discardBuilder.ToImmutable() };
        foreach (var card in picked)
        {
            s = AddInstanceToPile(s, card, effect.Pile!);
        }

        var ev = new BattleEvent(
            BattleEventKind.RecoverFromDiscard, Order: 0,
            CasterInstanceId: caster.InstanceId,
            Amount: picked.Count,
            Note: $"{select}:{effect.Pile}");
        return (s, new[] { ev });
    }

    /// <summary>
    /// 10.5.F: EnergyMax を永続的に増やす。当ターンの Energy はそのまま
    /// (StS 慣習: 次ターン開始時に EnergyMax まで補充される)。
    /// </summary>
    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyGainMaxEnergy(
        BattleState state, CombatActor caster, CardEffect effect)
    {
        if (effect.Scope != EffectScope.Self)
            throw new InvalidOperationException(
                $"gainMaxEnergy requires Scope=Self, got {effect.Scope}");

        var next = state with { EnergyMax = state.EnergyMax + effect.Amount };
        var ev = new BattleEvent(
            BattleEventKind.GainMaxEnergy, Order: 0,
            CasterInstanceId: caster.InstanceId,
            Amount: effect.Amount);
        return (next, new[] { ev });
    }

    /// <summary>
    /// 10.5.F: pile 名から BattleCardInstance を追加した state を返す。
    /// hand 上限超過時は discard に流す。draw は top (先頭) に挿入。
    /// </summary>
    private static BattleState AddInstanceToPile(
        BattleState state, BattleCardInstance instance, string pile) => pile switch
    {
        "hand" => state.Hand.Length < DrawHelper.HandCap
            ? state with { Hand = state.Hand.Add(instance) }
            : state with { DiscardPile = state.DiscardPile.Add(instance) },
        "draw"    => state with { DrawPile = state.DrawPile.Insert(0, instance) },
        "discard" => state with { DiscardPile = state.DiscardPile.Add(instance) },
        "exhaust" => state with { ExhaustPile = state.ExhaustPile.Add(instance) },
        _ => throw new InvalidOperationException(
            $"Unknown pile '{pile}', expected hand|draw|discard|exhaust"),
    };

    /// <summary>
    /// 10.5.F: addCard 等で生成する InstanceId 採番。RNG 非依存で衝突予防。
    /// 形式: "{cardRefId}-{Guid 8 char}"。
    /// </summary>
    private static string NewBattleInstanceId(string cardRefId)
        => $"{cardRefId}-{Guid.NewGuid().ToString("N").Substring(0, 8)}";

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
        // Why: 防御カードを「味方単体対象 (含 hero)」でも撃てるようにするため、
        // scope=Self 以外 (Single+Ally / All+Ally など) も ResolveTargets 経由で
        // 受ける。stale ref 対策で各 target を InstanceId で再取得する。
        // FakeRng は使わない (block は決定論)。
        IReadOnlyList<CombatActor> targets = effect.Scope == EffectScope.Self
            ? new[] { caster }
            : ResolveTargets(state, caster, effect, new RoguelikeCardGame.Core.Random.SystemRng(0));
        if (targets.Count == 0) return (state, Array.Empty<BattleEvent>());

        var s = state;
        var events = new List<BattleEvent>();
        int order = 0;
        foreach (var t in targets)
        {
            var current = FindActor(s, t.InstanceId);
            if (current is null) continue;
            var updated = current with { Block = current.Block.Add(effect.Amount) };
            s = ReplaceActor(s, t.InstanceId, updated);
            events.Add(new BattleEvent(
                BattleEventKind.GainBlock, Order: order++,
                CasterInstanceId: caster.InstanceId,
                TargetInstanceId: t.InstanceId,
                Amount: effect.Amount));
        }
        return (s, events);
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
                {
                    bool poolIsAllies = SideResolvesToAllies(caster, effect.Side.Value);
                    if (poolIsAllies)
                        return state.TargetAllyIndex is { } ai && ai < state.Allies.Length
                            ? new[] { state.Allies[ai] }
                            : Array.Empty<CombatActor>();
                    return state.TargetEnemyIndex is { } ei && ei < state.Enemies.Length
                        ? new[] { state.Enemies[ei] }
                        : Array.Empty<CombatActor>();
                }

            case EffectScope.Random:
            {
                if (effect.Side is null)
                    throw new InvalidOperationException(
                        $"effect '{effect.Action}' Scope=Random requires non-null Side");
                var pool = ResolveSidePool(state, caster, effect.Side.Value)
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
                return ResolveSidePool(state, caster, effect.Side.Value)
                    .Where(a => a.IsAlive).ToList();
            }
        }
        return Array.Empty<CombatActor>();
    }

    /// <summary>
    /// CardEffect.Side は「行動主体からの相対視点」。caster の側に応じて
    /// state.Allies / state.Enemies のどちらを対象 pool にするか決める。
    /// </summary>
    private static ImmutableArray<CombatActor> ResolveSidePool(
        BattleState state, CombatActor caster, EffectSide side)
    {
        bool casterIsAlly = caster.Side == ActorSide.Ally;
        bool sideMeansAllyPool = (casterIsAlly && side == EffectSide.Ally)
            || (!casterIsAlly && side == EffectSide.Enemy);
        return sideMeansAllyPool ? state.Allies : state.Enemies;
    }

    private static bool SideResolvesToAllies(CombatActor caster, EffectSide side)
    {
        bool casterIsAlly = caster.Side == ActorSide.Ally;
        return (casterIsAlly && side == EffectSide.Ally)
            || (!casterIsAlly && side == EffectSide.Enemy);
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
        // heal は ApplyHeal の入口で Scope=Self または Side=Ally を要求済み。
        // Side=Ally は「caster の味方側」(player なら state.Allies / 敵なら state.Enemies)。
        switch (effect.Scope)
        {
            case EffectScope.Self:
                return new[] { caster };

            case EffectScope.Single:
            {
                bool poolIsAllies = SideResolvesToAllies(caster, EffectSide.Ally);
                if (poolIsAllies)
                    return state.TargetAllyIndex is { } ai && ai < state.Allies.Length
                        ? new[] { state.Allies[ai] }
                        : (IReadOnlyList<CombatActor>)Array.Empty<CombatActor>();
                return state.TargetEnemyIndex is { } ei && ei < state.Enemies.Length
                    ? new[] { state.Enemies[ei] }
                    : (IReadOnlyList<CombatActor>)Array.Empty<CombatActor>();
            }

            case EffectScope.Random:
                return PickRandomAlive(ResolveSidePool(state, caster, EffectSide.Ally), rng);

            case EffectScope.All:
                return ResolveSidePool(state, caster, EffectSide.Ally)
                    .Where(a => a.IsAlive).ToList();

            default:
                return Array.Empty<CombatActor>();
        }
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

        var newState = DrawHelper.Draw(state, effect.Amount, rng, out int actualDrawn);
        if (actualDrawn == 0) return (state, Array.Empty<BattleEvent>());

        var evs = new[] {
            new BattleEvent(BattleEventKind.Draw, Order: 0,
                CasterInstanceId: caster.InstanceId, Amount: actualDrawn),
        };
        return (newState, evs);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyDiscard(
        BattleState state, CombatActor caster, CardEffect effect, IRng rng,
        DataCatalog catalog)
    {
        // Phase 10.5.F: Select 優先パス。Select 指定があれば新ロジック。
        // Select=null は既存 Scope ベース挙動 (後方互換)。
        if (!string.IsNullOrEmpty(effect.Select))
        {
            return ApplyDiscardWithSelect(state, caster, effect, rng, catalog);
        }

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
            return BuildResult(state, caster, hand, discard, discardedCount, note, rng, catalog);
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
            return BuildResult(state, caster, hand, discard, target, note, rng, catalog);
        }
    }

    /// <summary>
    /// 10.5.F: discard の Select 対応版。Select=all/random/choose を解釈する。
    /// M6.9: choose は UI 入力フロー未実装のため、暫定的に random と同じ挙動で fallback。
    ///   将来 Phase 10.5.M で正式実装予定。
    /// </summary>
    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyDiscardWithSelect(
        BattleState state, CombatActor caster, CardEffect effect, IRng rng,
        DataCatalog catalog)
    {
        if (state.Hand.Length == 0) return (state, Array.Empty<BattleEvent>());

        var hand = state.Hand.ToBuilder();
        var discard = state.DiscardPile.ToBuilder();
        int target;
        string note;

        if (effect.Select == "all")
        {
            target = hand.Count;
            foreach (var c in hand) discard.Add(c);
            hand.Clear();
            note = "all";
        }
        else // "random" or unknown → random
        {
            target = Math.Min(effect.Amount, hand.Count);
            for (int i = 0; i < target; i++)
            {
                int idx = rng.NextInt(0, hand.Count);
                var card = hand[idx];
                hand.RemoveAt(idx);
                discard.Add(card);
            }
            note = "random";
        }

        return BuildResult(state, caster, hand, discard, target, note, rng, catalog);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) BuildResult(
        BattleState state, CombatActor caster,
        ImmutableArray<BattleCardInstance>.Builder hand,
        ImmutableArray<BattleCardInstance>.Builder discard,
        int discardedCount, string note,
        IRng rng, DataCatalog catalog)
    {
        var next = state with
        {
            Hand = hand.ToImmutable(),
            DiscardPile = discard.ToImmutable(),
        };
        if (discardedCount == 0) return (next, Array.Empty<BattleEvent>());
        var events = new List<BattleEvent>
        {
            new BattleEvent(BattleEventKind.Discard, Order: 0,
                CasterInstanceId: caster.InstanceId,
                Amount: discardedCount, Note: note),
        };
        // Phase 10.5.L1.5: OnCardDiscarded relic / power 発火
        return FireOnCardDiscarded(next, events, rng, catalog);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyExhaustCard(
        BattleState state, CombatActor caster, CardEffect effect, IRng rng,
        DataCatalog catalog)
    {
        // Phase 10.5.M2: Select 対応
        // - "all": 当該 pile を全て除外 (Amount 無視)
        // - "random" (or null): N 枚ランダム除外 (既存挙動)
        // - "choose": UI input が必要。本フェーズではプレイヤー選択 modal が未実装の
        //   ため、暫定的に random と同じ挙動 (N 枚ランダム除外) で fallback する。
        //   description には「選んで除外」と表示されるが engine ロジックは random。
        //   TODO: Phase 10.5.M で UI 選択フローを正式実装する (BattleEngine 側で
        //   Pending state を持ち、Client が choice confirm を返す経路)。
        // 旧仕様: NotImplementedException → Server 500 になり「叡智の奔流(強化版)」
        //   等の choose 系カードがプレイ不能だった (M6.9 ユーザー報告)。

        var (sourceBuilder, exhaustBuilder, applyResult) = OpenPile(state, effect.Pile);

        int target;
        if (effect.Select == "all")
        {
            target = sourceBuilder.Count;
            foreach (var c in sourceBuilder) exhaustBuilder.Add(c);
            sourceBuilder.Clear();
        }
        else
        {
            target = Math.Min(effect.Amount, sourceBuilder.Count);
            for (int i = 0; i < target; i++)
            {
                int idx = rng.NextInt(0, sourceBuilder.Count);
                var card = sourceBuilder[idx];
                sourceBuilder.RemoveAt(idx);
                exhaustBuilder.Add(card);
            }
        }

        if (target == 0)
            return (state, Array.Empty<BattleEvent>());

        var next = applyResult(sourceBuilder, exhaustBuilder);
        var events = new List<BattleEvent>
        {
            new BattleEvent(
                BattleEventKind.Exhaust, Order: 0,
                CasterInstanceId: caster.InstanceId,
                Amount: target, Note: effect.Pile),
        };
        // Phase 10.5.L1.5: OnCardExhausted relic / power 発火
        return FireOnCardExhausted(next, events, rng, catalog);
    }

    /// <summary>
    /// pile 名 (hand/discard/draw) から source / exhaust の Builder と適用関数を返す。
    /// 不正 pile / null は InvalidOperationException。
    /// </summary>
    private static (
        ImmutableArray<BattleCardInstance>.Builder source,
        ImmutableArray<BattleCardInstance>.Builder exhaust,
        Func<ImmutableArray<BattleCardInstance>.Builder,
             ImmutableArray<BattleCardInstance>.Builder, BattleState> apply
    ) OpenPile(BattleState state, string? pileName)
    {
        var exhaustBuilder = state.ExhaustPile.ToBuilder();
        // Phase 10.5.M6.7: pile 未指定 (null / 空文字) は "hand" として扱う。
        //  CardTextFormatter.ZoneJp も同じく null/empty を「手札」とフォールバック
        //  するため、formatter 出力と engine 動作の一貫性を確保。
        return pileName switch
        {
            null or "" or "hand" => (state.Hand.ToBuilder(), exhaustBuilder,
                (s, e) => state with { Hand = s.ToImmutable(), ExhaustPile = e.ToImmutable() }),
            "discard" => (state.DiscardPile.ToBuilder(), exhaustBuilder,
                (s, e) => state with { DiscardPile = s.ToImmutable(), ExhaustPile = e.ToImmutable() }),
            "draw" => (state.DrawPile.ToBuilder(), exhaustBuilder,
                (s, e) => state with { DrawPile = s.ToImmutable(), ExhaustPile = e.ToImmutable() }),
            _ => throw new InvalidOperationException($"exhaustCard invalid Pile '{pileName}', expected hand|discard|draw"),
        };
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyUpgrade(
        BattleState state, CombatActor caster, CardEffect effect, IRng rng,
        DataCatalog catalog)
    {
        // Phase 10.5.M2: Select 対応
        // - "all": pile 内の強化可能カードを全て強化 (Amount 無視)
        // - "random" (or null): N 枚ランダム強化 (既存挙動)
        // - "choose": UI input が未実装のため、暫定的に random と同じ挙動で fallback。
        //   M6.9: exhaustCard と同様、UI 選択フロー実装まで random fallback で動かす。

        // Pile 検証は OpenSourcePile で（exhaust pile は使わない）
        var (sourceBuilder, applyResult) = OpenSourcePile(state, effect.Pile);

        // 強化候補抽出: IsUpgraded=false かつ definition.IsUpgradable
        var candidates = new List<int>();
        for (int i = 0; i < sourceBuilder.Count; i++)
        {
            var card = sourceBuilder[i];
            if (card.IsUpgraded) continue;
            if (!catalog.TryGetCard(card.CardDefinitionId, out var def)) continue;
            if (def.UpgradedCost is null && def.UpgradedEffects is null) continue;
            candidates.Add(i);
        }

        int target = effect.Select == "all"
            ? candidates.Count
            : Math.Min(effect.Amount, candidates.Count);
        int upgradedCount = 0;
        for (int i = 0; i < target; i++)
        {
            int pickIdx = effect.Select == "all" ? 0 : rng.NextInt(0, candidates.Count);
            int sourceIdx = candidates[pickIdx];
            candidates.RemoveAt(pickIdx);
            var card = sourceBuilder[sourceIdx];
            sourceBuilder[sourceIdx] = card with { IsUpgraded = true };
            upgradedCount++;
        }

        if (upgradedCount == 0)
            return (state, Array.Empty<BattleEvent>());

        var next = applyResult(sourceBuilder);
        var ev = new BattleEvent(
            BattleEventKind.Upgrade, Order: 0,
            CasterInstanceId: caster.InstanceId,
            Amount: upgradedCount, Note: effect.Pile);
        return (next, new[] { ev });
    }

    private static (
        ImmutableArray<BattleCardInstance>.Builder source,
        Func<ImmutableArray<BattleCardInstance>.Builder, BattleState> apply
    ) OpenSourcePile(BattleState state, string? pileName)
    {
        return pileName switch
        {
            "hand" => (state.Hand.ToBuilder(),
                s => state with { Hand = s.ToImmutable() }),
            "discard" => (state.DiscardPile.ToBuilder(),
                s => state with { DiscardPile = s.ToImmutable() }),
            "draw" => (state.DrawPile.ToBuilder(),
                s => state with { DrawPile = s.ToImmutable() }),
            null => throw new InvalidOperationException("upgrade requires Pile (hand|discard|draw)"),
            _ => throw new InvalidOperationException($"upgrade invalid Pile '{pileName}', expected hand|discard|draw"),
        };
    }

    /// <summary>
    /// 10.2.D: summon action。effect.UnitId のキャラを catalog から取り出し、
    /// state.Allies の空き slot (1〜3) に追加する。null/未知 UnitId は throw。slot 満杯時は silent skip。
    /// AssociatedSummonHeldInstanceId は null（PlayCard 側 card-move logic が後で設定）。
    /// </summary>
    private static (BattleState, IReadOnlyList<BattleEvent>) ApplySummon(
        BattleState state, CombatActor caster, CardEffect effect, IRng rng, DataCatalog catalog)
    {
        if (string.IsNullOrEmpty(effect.UnitId))
            throw new InvalidOperationException("summon requires UnitId");
        if (!catalog.TryGetUnit(effect.UnitId, out var unitDef))
            throw new InvalidOperationException($"summon unknown UnitId '{effect.UnitId}'");

        // 空き slot 検索（hero=0 を除く 1〜3）
        var occupiedSlots = state.Allies.Select(a => a.SlotIndex).ToHashSet();
        int emptySlot = -1;
        for (int i = 1; i <= 3; i++)
        {
            if (!occupiedSlots.Contains(i)) { emptySlot = i; break; }
        }
        if (emptySlot == -1)
            return (state, Array.Empty<BattleEvent>());  // 不発、silent skip

        string newInstanceId = $"summon_inst_{state.Turn}_{rng.NextInt(0, 1 << 30):x}";
        var newActor = new CombatActor(
            InstanceId: newInstanceId,
            DefinitionId: effect.UnitId,
            Side: ActorSide.Ally,
            SlotIndex: emptySlot,
            CurrentHp: unitDef.Hp,
            MaxHp: unitDef.Hp,
            Block: BlockPool.Empty,
            AttackSingle: AttackPool.Empty,
            AttackRandom: AttackPool.Empty,
            AttackAll: AttackPool.Empty,
            Statuses: ImmutableDictionary<string, int>.Empty,
            CurrentMoveId: unitDef.InitialMoveId,
            RemainingLifetimeTurns: unitDef.LifetimeTurns,
            AssociatedSummonHeldInstanceId: null);   // PlayCard card-move logic で設定

        var next = state with { Allies = state.Allies.Add(newActor) };
        var ev = new BattleEvent(
            BattleEventKind.Summon, Order: 0,
            CasterInstanceId: caster.InstanceId,
            TargetInstanceId: newInstanceId,
            Note: effect.UnitId);
        return (next, new[] { ev });
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
