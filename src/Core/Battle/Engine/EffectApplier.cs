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
    /// <summary>
    /// 負値も意味を持つ「permanent stat」系ステータス。0 のみ remove、それ以外は signed 値で保持。
    /// それ以外 (weak / vulnerable / poison 等の ailment) は 0 以下で remove する従来挙動。
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<string> SignedStatuses = new() { "strength", "dexterity" };

    public static (BattleState, IReadOnlyList<BattleEvent>) Apply(
        BattleState state, CombatActor caster, CardEffect effect, IRng rng,
        DataCatalog catalog,
        CombatSource? source = null,
        DamageCauseChain? causeChain = null,
        string? playingCardInstanceId = null)                // 10.2.D
    {
        // 10.5.D: AmountSource を runtime 値で resolve。null 指定の effect は素通し。
        var resolved = ResolveAmount(effect, state, caster);

        return resolved.Action switch
        {
            "attack" => ApplyAttack(state, caster, resolved, source, causeChain),
            "block"  => ApplyBlock(state, caster, resolved),
            "buff"   => ApplyStatusChange(state, caster, resolved, rng, source),
            "debuff" => ApplyStatusChange(state, caster, resolved, rng, source),
            "heal"   => ApplyHeal(state, caster, resolved, rng),
            "draw"   => ApplyDraw(state, caster, resolved, rng),
            // Why: 既存 reward_*.json の draw 効果カードは "drawCards" 記法で
            // 書かれており、potion 側 "draw" と表記揺れしていた。両方サポート
            // することで洞察 / 叡智の奔流 / 戦術的撤退 等の効果が反映される。
            "drawCards" => ApplyDraw(state, caster, resolved, rng),
            "discard"=> ApplyDiscard(
                state, caster, resolved, rng, catalog,
                playingCardInstanceId),
            "exhaustSelf" => ApplyExhaustSelf(
                state, caster, rng, catalog, playingCardInstanceId),
            "retainSelf"  => (state, Array.Empty<BattleEvent>()),
            "gainEnergy"  => ApplyGainEnergy(state, caster, resolved),
            "exhaustCard" => ApplyExhaustCard(
                state, caster, resolved, rng, catalog,
                playingCardInstanceId),
            "upgrade"     => ApplyUpgrade(
                state, caster, resolved, rng, catalog,
                playingCardInstanceId),
            "summon"      => ApplySummon(state, caster, resolved, rng, catalog, source),
            // Phase 10.5.F: engine 新 actions
            "selfDamage"        => ApplySelfDamage(
                state, caster, resolved, rng, catalog, source,
                playingCardInstanceId),
            "addCard"           => ApplyAddCard(state, caster, resolved),
            "recoverFromDiscard"=> ApplyRecoverFromDiscard(state, caster, resolved, rng),
            "gainMaxEnergy"     => ApplyGainMaxEnergy(state, caster, resolved),
            "modifyCardCost"    => ApplyModifyCardCost(
                state, caster, resolved, rng, source,
                playingCardInstanceId),
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
        BattleState state, CombatActor caster, IRng rng, DataCatalog catalog,
        string? playingCardInstanceId)
    {
        var ev = new BattleEvent(
            BattleEventKind.Exhaust, Order: 0,
            CasterInstanceId: caster.InstanceId,
            Amount: 1, Note: "self");
        var events = new List<BattleEvent> { ev };
        // Phase 10.5.L1.5: OnCardExhausted relic / power 発火
        return FireOnCardExhausted(
            state, events, rng, catalog, playingCardInstanceId);
    }

    /// <summary>
    /// Phase 10.5.L1.5: discard/exhaust の末尾で OnCardDiscarded / OnCardExhausted の
    /// Relic + Power を fire する共通ヘルパ。base events に新規 events を append し、
    /// state を更新して返す。
    /// </summary>
    private static (BattleState, IReadOnlyList<BattleEvent>) FireOnCardDiscarded(
        BattleState state, List<BattleEvent> baseEvents, IRng rng,
        DataCatalog catalog, string? playingCardInstanceId)
        => FireTrigger(
            state, baseEvents, "OnCardDiscarded", rng, catalog,
            playingCardInstanceId);

    private static (BattleState, IReadOnlyList<BattleEvent>) FireOnCardExhausted(
        BattleState state, List<BattleEvent> baseEvents, IRng rng,
        DataCatalog catalog, string? playingCardInstanceId)
        => FireTrigger(
            state, baseEvents, "OnCardExhausted", rng, catalog,
            playingCardInstanceId);

    private static (BattleState, IReadOnlyList<BattleEvent>) FireTrigger(
        BattleState state, List<BattleEvent> baseEvents, string trigger,
        IRng rng, DataCatalog catalog, string? playingCardInstanceId)
    {
        var s = state;
        var (afterRelic, evsRelic) = RelicTriggerProcessor.Fire(
            s, trigger, catalog, rng, orderStart: baseEvents.Count,
            playingCardInstanceId: playingCardInstanceId);
        s = afterRelic;
        baseEvents.AddRange(evsRelic);
        var (afterPower, evsPower) = PowerTriggerProcessor.Fire(
            s, trigger, catalog, rng, orderStart: baseEvents.Count,
            playingCardInstanceId: playingCardInstanceId);
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
        var next = state with { Energy = EnergyMath.AddSaturating(state.Energy, effect.Amount) };
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
        IRng rng, DataCatalog catalog, CombatSource? source,
        string? playingCardInstanceId)
    {
        if (effect.Scope != EffectScope.Self)
            throw new InvalidOperationException(
                $"selfDamage requires Scope=Self, got {effect.Scope}");

        var current = FindActor(state, caster.InstanceId) ?? caster;
        int damage = Math.Max(0, effect.Amount);
        int hpBefore = Math.Max(0, current.CurrentHp);
        int actualHpLoss = Math.Min(hpBefore, damage);
        int overkill = Math.Max(0, damage - hpBefore);
        int newHp = Math.Max(0, current.CurrentHp - damage);
        var updated = current with { CurrentHp = newHp };
        var next = ReplaceActor(state, caster.InstanceId, updated);
        var resolvedSource = source ?? new CombatSource(
            CombatSourceKind.Unattributed,
            current.OwnerPlayerSlot ?? (current.DefinitionId == "hero" ? current.SlotIndex : null),
            current.OwnerActorInstanceId ?? (current.DefinitionId == "hero" ? current.InstanceId : null),
            current.InstanceId);
        var context = new HitContext(
            DamageType.SelfDamage, resolvedSource,
            new DamageCauseChain(ImmutableArray.Create(new DamageCauseFrame(
                0, DamageType.SelfDamage, resolvedSource, null, "selfDamage"))));

        var events = new List<BattleEvent>
        {
            new(BattleEventKind.DealDamage, Order: 0,
                CasterInstanceId: caster.InstanceId,
                TargetInstanceId: caster.InstanceId,
                Amount: damage,
                Note: "selfDamage", DamageType: DamageType.SelfDamage,
                Source: resolvedSource, CauseChain: context.CauseChain,
                ActualHpLoss: actualHpLoss, Overkill: overkill,
                HitLanded: damage > 0),
        };
        int order = 1;
        if (current.IsAlive && !updated.IsAlive)
        {
            events.Add(new BattleEvent(
                BattleEventKind.ActorDeath, Order: order++,
                CasterInstanceId: caster.InstanceId,
                TargetInstanceId: caster.InstanceId,
                Note: "selfDamage", DamageType: DamageType.SelfDamage,
                Source: resolvedSource, CauseChain: context.CauseChain,
                ActualHpLoss: actualHpLoss, Overkill: overkill,
                HitLanded: damage > 0));
        }
        else if (updated.DefinitionId == "hero" && updated.IsAlive && actualHpLoss > 0)
        {
            // 10.5.E: hero に damage が入った直後 OnDamageReceived power fire
            var (afterPower, evsPower) = PowerTriggerProcessor.FireOnDamageReceived(
                next, catalog, rng, orderStart: order,
                playingCardInstanceId: playingCardInstanceId);
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
    /// 10.5.F: ターン開始時の Energy 追加量（互換名 EnergyMax）を永続的に増やす。
    /// 当ターンの Energy はそのままで、次ターン以降の持越し加算へ反映する。
    /// </summary>
    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyGainMaxEnergy(
        BattleState state, CombatActor caster, CardEffect effect)
    {
        if (effect.Scope != EffectScope.Self)
            throw new InvalidOperationException(
                $"gainMaxEnergy requires Scope=Self, got {effect.Scope}");

        var next = state with
        {
            EnergyMax = EnergyMath.AddSaturating(state.EnergyMax, effect.Amount),
        };
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
        BattleState state, CombatActor caster, CardEffect effect, CombatSource? source,
        DamageCauseChain? causeChain)
    {
        // stale ref 対策: state から InstanceId で最新の actor を再取得する
        var current = FindActor(state, caster.InstanceId) ?? caster;
        var hit = new AttackHit(
            effect.Amount,
            source ?? CombatSource.Unattributed,
            causeChain ?? DamageCauseChain.Empty);
        var updated = effect.Scope switch
        {
            EffectScope.Single => current with { AttackSingle = current.AttackSingle.Add(hit) },
            EffectScope.Random => current with { AttackRandom = current.AttackRandom.Add(hit) },
            EffectScope.All    => current with { AttackAll    = current.AttackAll.Add(hit) },
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
        BattleState state, CombatActor caster, CardEffect effect, IRng rng,
        CombatSource? source)
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

            // Phase 10.6.B フォローアップ: strength / dexterity は signed (負値も保持)。
            // weak / vulnerable / poison 等の ailment は従来どおり 0 以下で remove。
            bool isSigned = SignedStatuses.Contains(effect.Name);
            ImmutableDictionary<string, int> newStatuses;
            if (isSigned)
            {
                // signed: 0 のみ remove、それ以外 (正/負) は保持
                newStatuses = newAmount == 0
                    ? current.Statuses.Remove(effect.Name)
                    : current.Statuses.SetItem(effect.Name, newAmount);
            }
            else
            {
                // unsigned: 0 以下で remove (既存挙動)
                newStatuses = newAmount <= 0
                    ? current.Statuses.Remove(effect.Name)
                    : current.Statuses.SetItem(effect.Name, newAmount);
            }

            var statusSources = current.StatusSources;
            bool statusRemoved = isSigned ? newAmount == 0 : newAmount <= 0;
            if (statusRemoved)
            {
                statusSources = statusSources.Remove(effect.Name);
            }
            else if (effect.Amount > 0)
            {
                // The most recent positive application owns future status ticks.
                // A null producer deliberately replaces any stale owner with
                // Unattributed instead of crediting the previous applier.
                statusSources = statusSources.SetItem(
                    effect.Name, source ?? CombatSource.Unattributed);
            }

            var updated = current with
            {
                Statuses = newStatuses,
                StatusSources = statusSources,
            };
            s = ReplaceActor(s, tid, updated);

            // signed 系は値変化時に常に ApplyStatus 発火 (負値含む)
            // unsigned 系は newAmount > 0 のときのみ ApplyStatus
            bool didApply = isSigned
                ? (newAmount != 0 && currentAmount != newAmount)
                : (newAmount > 0 && currentAmount != newAmount);
            bool didRemove = isSigned
                ? (newAmount == 0 && currentAmount != 0)
                : (newAmount <= 0 && currentAmount > 0);

            if (didApply)
            {
                events.Add(new BattleEvent(
                    BattleEventKind.ApplyStatus, Order: order++,
                    CasterInstanceId: caster.InstanceId,
                    TargetInstanceId: tid,
                    Amount: effect.Amount,
                    Note: effect.Name,
                    Source: source ?? CombatSource.Unattributed));
            }
            else if (didRemove)
            {
                events.Add(new BattleEvent(
                    BattleEventKind.RemoveStatus, Order: order++,
                    TargetInstanceId: tid,
                    Note: effect.Name,
                    Source: source ?? CombatSource.Unattributed));
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
        DataCatalog catalog, string? excludedHandInstanceId)
    {
        // Phase 10.5.F: Select 優先パス。Select 指定があれば新ロジック。
        // Select=null は既存 Scope ベース挙動 (後方互換)。
        if (!string.IsNullOrEmpty(effect.Select))
        {
            return ApplyDiscardWithSelect(
                state, caster, effect, rng, catalog, excludedHandInstanceId);
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
            int discardedCount = MoveAllEligible(
                hand, discard, excludedHandInstanceId);
            return BuildResult(
                state, caster, hand, discard, discardedCount, note, rng,
                catalog, excludedHandInstanceId);
        }
        else // Random
        {
            note = "random";
            int target = MoveRandomEligible(
                hand, discard, effect.Amount, excludedHandInstanceId, rng);
            return BuildResult(
                state, caster, hand, discard, target, note, rng, catalog,
                excludedHandInstanceId);
        }
    }

    /// <summary>
    /// 10.5.F: discard の Select 対応版。Select=all/random/choose を解釈する。
    /// M6.9: choose は UI 入力フロー未実装のため、暫定的に random と同じ挙動で fallback。
    ///   将来 Phase 10.5.M で正式実装予定。
    /// </summary>
    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyDiscardWithSelect(
        BattleState state, CombatActor caster, CardEffect effect, IRng rng,
        DataCatalog catalog, string? excludedHandInstanceId)
    {
        if (state.Hand.Length == 0) return (state, Array.Empty<BattleEvent>());

        var hand = state.Hand.ToBuilder();
        var discard = state.DiscardPile.ToBuilder();
        int target;
        string note;

        if (effect.Select == "all")
        {
            target = MoveAllEligible(hand, discard, excludedHandInstanceId);
            note = "all";
        }
        else // "random" or unknown → random
        {
            target = MoveRandomEligible(
                hand, discard, effect.Amount, excludedHandInstanceId, rng);
            note = "random";
        }

        return BuildResult(
            state, caster, hand, discard, target, note, rng, catalog,
            excludedHandInstanceId);
    }

    /// <summary>
    /// A card remains in Hand until all of its effects finish. Hand mutations
    /// must therefore treat that in-flight card as unavailable, just like the
    /// choose-candidate path does.
    /// </summary>
    private static int MoveAllEligible(
        ImmutableArray<BattleCardInstance>.Builder source,
        ImmutableArray<BattleCardInstance>.Builder destination,
        string? excludedInstanceId)
    {
        int moved = 0;
        int destinationStart = destination.Count;
        for (int i = source.Count - 1; i >= 0; i--)
        {
            if (source[i].InstanceId == excludedInstanceId) continue;

            // Reverse traversal plus insertion at the original destination tail
            // preserves the source order used by the previous implementation.
            destination.Insert(destinationStart, source[i]);
            source.RemoveAt(i);
            moved++;
        }
        return moved;
    }

    private static int MoveRandomEligible(
        ImmutableArray<BattleCardInstance>.Builder source,
        ImmutableArray<BattleCardInstance>.Builder destination,
        int amount,
        string? excludedInstanceId,
        IRng rng)
    {
        var eligibleInstanceIds = new List<string>();
        foreach (var card in source)
        {
            if (card.InstanceId != excludedInstanceId)
                eligibleInstanceIds.Add(card.InstanceId);
        }

        int target = Math.Min(amount, eligibleInstanceIds.Count);
        for (int i = 0; i < target; i++)
        {
            int eligibleIndex = rng.NextInt(0, eligibleInstanceIds.Count);
            string instanceId = eligibleInstanceIds[eligibleIndex];
            eligibleInstanceIds.RemoveAt(eligibleIndex);

            int sourceIndex = -1;
            for (int j = 0; j < source.Count; j++)
            {
                if (source[j].InstanceId == instanceId)
                {
                    sourceIndex = j;
                    break;
                }
            }
            if (sourceIndex < 0) continue;

            var card = source[sourceIndex];
            source.RemoveAt(sourceIndex);
            destination.Add(card);
        }
        return target;
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) BuildResult(
        BattleState state, CombatActor caster,
        ImmutableArray<BattleCardInstance>.Builder hand,
        ImmutableArray<BattleCardInstance>.Builder discard,
        int discardedCount, string note,
        IRng rng, DataCatalog catalog, string? playingCardInstanceId)
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
        return FireOnCardDiscarded(
            next, events, rng, catalog, playingCardInstanceId);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyExhaustCard(
        BattleState state, CombatActor caster, CardEffect effect, IRng rng,
        DataCatalog catalog, string? excludedHandInstanceId)
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
            target = MoveAllEligible(
                sourceBuilder, exhaustBuilder, excludedHandInstanceId);
        }
        else
        {
            target = MoveRandomEligible(
                sourceBuilder, exhaustBuilder, effect.Amount,
                excludedHandInstanceId, rng);
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
        return FireOnCardExhausted(
            next, events, rng, catalog, excludedHandInstanceId);
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
        DataCatalog catalog, string? excludedHandInstanceId)
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
            if (card.InstanceId == excludedHandInstanceId) continue;
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
        BattleState state, CombatActor caster, CardEffect effect, IRng rng, DataCatalog catalog,
        CombatSource? source)
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
            AssociatedSummonHeldInstanceId: null)    // PlayCard card-move logic で設定
        {
            OwnerPlayerSlot = source?.OwnerSlot
                ?? caster.OwnerPlayerSlot
                ?? (caster.DefinitionId == "hero" ? caster.SlotIndex : null),
            OwnerActorInstanceId = source?.OwnerInstanceId
                ?? caster.OwnerActorInstanceId
                ?? (caster.DefinitionId == "hero" ? caster.InstanceId : null),
        };

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

    // ---- modifyCardCost ----

    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyModifyCardCost(
        BattleState state, CombatActor caster, CardEffect effect, IRng rng,
        CombatSource? source, string? excludedHandInstanceId)
    {
        var duration = ValidateModifyCardCost(effect);
        RejectUnsupportedModifyCardCostChoose(effect, source);
        var (pileBuilder, applyResult) = OpenCardCostPile(state, effect.Pile!);
        var eligibleIds = EligibleCardInstanceIds(
            pileBuilder, effect.Pile!, excludedHandInstanceId);

        IReadOnlyList<string> selectedIds;
        switch (effect.Select)
        {
            case "all":
                selectedIds = eligibleIds;
                break;
            case "random":
                selectedIds = PickRandomCardInstanceIds(
                    eligibleIds, ModifyCardSelectionCount(effect), rng);
                break;
            case "choose":
                int count = ModifyCardSelectionCount(effect);
                if (eligibleIds.Count > count)
                    throw new InvalidOperationException(
                        "modifyCardCost select='choose' requires a PendingCardPlay choice");
                // No decision is needed when every candidate must be selected. Preserve pile
                // order and do not consume RNG in that auto-apply path.
                selectedIds = eligibleIds;
                break;
            default:
                throw new InvalidOperationException(
                    $"modifyCardCost invalid Select '{effect.Select}', expected all|random|choose");
        }

        return ApplyCardCostModifierToSelected(
            state, caster, effect, duration, pileBuilder, applyResult,
            selectedIds, source);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyModifyCardCostChosen(
        BattleState state, CombatActor caster, CardEffect effect,
        ImmutableArray<string> selectedInstanceIds, CombatSource? source)
    {
        var duration = ValidateModifyCardCost(effect);
        RejectUnsupportedModifyCardCostChoose(effect, source);
        if (effect.Select != "choose")
            throw new InvalidOperationException(
                $"modifyCardCost chosen path requires Select='choose', got '{effect.Select}'");
        var (pileBuilder, applyResult) = OpenCardCostPile(state, effect.Pile!);
        return ApplyCardCostModifierToSelected(
            state, caster, effect, duration, pileBuilder, applyResult,
            selectedInstanceIds, source);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyCardCostModifierToSelected(
        BattleState state,
        CombatActor caster,
        CardEffect effect,
        CardCostModifierDuration duration,
        ImmutableArray<BattleCardInstance>.Builder pileBuilder,
        Func<ImmutableArray<BattleCardInstance>.Builder, BattleState> applyResult,
        IReadOnlyList<string> selectedInstanceIds,
        CombatSource? source)
    {
        if (selectedInstanceIds.Count == 0)
            return (state, Array.Empty<BattleEvent>());

        var events = new List<BattleEvent>();
        var appliedIds = new HashSet<string>(StringComparer.Ordinal);
        string? sourceId = source?.InstanceId
            ?? source?.DefinitionId
            ?? source?.ActorInstanceId;
        var modifier = new CardCostModifier(effect.Amount, duration, sourceId);
        string note = $"{effect.CostDuration}:{effect.Pile}";

        foreach (var instanceId in selectedInstanceIds)
        {
            if (!appliedIds.Add(instanceId)) continue;
            int index = -1;
            for (int i = 0; i < pileBuilder.Count; i++)
            {
                if (pileBuilder[i].InstanceId == instanceId)
                {
                    index = i;
                    break;
                }
            }
            if (index < 0) continue;

            pileBuilder[index] = pileBuilder[index].AddCostModifier(modifier);
            events.Add(new BattleEvent(
                BattleEventKind.ModifyCardCost,
                Order: events.Count,
                CasterInstanceId: caster.InstanceId,
                TargetInstanceId: instanceId,
                Amount: effect.Amount,
                Note: note,
                Source: source));
        }

        if (events.Count == 0)
            return (state, Array.Empty<BattleEvent>());
        return (applyResult(pileBuilder), events);
    }

    private static CardCostModifierDuration ValidateModifyCardCost(CardEffect effect)
    {
        if (effect.Scope != EffectScope.Self)
            throw new InvalidOperationException(
                $"modifyCardCost requires Scope=Self, got {effect.Scope}");
        if (effect.Amount == 0)
            throw new InvalidOperationException(
                "modifyCardCost Amount must not be 0");
        if (effect.CardCount is <= 0)
            throw new InvalidOperationException(
                $"modifyCardCost CardCount must be positive, got {effect.CardCount}");
        if (effect.Pile is not ("hand" or "draw" or "discard" or "exhaust"))
            throw new InvalidOperationException(
                $"modifyCardCost invalid Pile '{effect.Pile}', expected hand|draw|discard|exhaust");
        if (effect.Select is not ("all" or "random" or "choose"))
            throw new InvalidOperationException(
                $"modifyCardCost invalid Select '{effect.Select}', expected all|random|choose");
        return effect.CostDuration switch
        {
            "untilPlayed" => CardCostModifierDuration.UntilPlayed,
            "untilTurnEnd" => CardCostModifierDuration.UntilTurnEnd,
            _ => throw new InvalidOperationException(
                $"modifyCardCost invalid CostDuration '{effect.CostDuration}', " +
                "expected untilPlayed|untilTurnEnd"),
        };
    }

    private static void RejectUnsupportedModifyCardCostChoose(
        CardEffect effect, CombatSource? source = null)
    {
        if (effect.Select != "choose") return;
        bool isTriggered = !string.IsNullOrEmpty(effect.Trigger)
            || source is { Kind: not CombatSourceKind.CardEffect };
        if (isTriggered)
            throw new InvalidOperationException(
                "Triggered or non-card modifyCardCost effects cannot use Select='choose' " +
                "because their resolution cannot create PendingCardPlay");
    }

    private static int ModifyCardSelectionCount(CardEffect effect) =>
        effect.CardCount ?? 1;

    private static int ChooseSelectionCount(CardEffect effect) =>
        effect.Action == "modifyCardCost"
            ? ModifyCardSelectionCount(effect)
            : effect.Amount;

    private static List<string> EligibleCardInstanceIds(
        ImmutableArray<BattleCardInstance>.Builder pileBuilder,
        string pile,
        string? excludedHandInstanceId)
    {
        var ids = new List<string>(pileBuilder.Count);
        foreach (var card in pileBuilder)
        {
            if (pile == "hand" && card.InstanceId == excludedHandInstanceId)
                continue;
            ids.Add(card.InstanceId);
        }
        return ids;
    }

    private static IReadOnlyList<string> PickRandomCardInstanceIds(
        IReadOnlyList<string> eligibleIds, int count, IRng rng)
    {
        var remaining = new List<string>(eligibleIds);
        var selected = new List<string>(Math.Min(count, remaining.Count));
        int target = Math.Min(count, remaining.Count);
        for (int i = 0; i < target; i++)
        {
            int index = rng.NextInt(0, remaining.Count);
            selected.Add(remaining[index]);
            remaining.RemoveAt(index);
        }
        return selected;
    }

    private static (
        ImmutableArray<BattleCardInstance>.Builder pile,
        Func<ImmutableArray<BattleCardInstance>.Builder, BattleState> apply
    ) OpenCardCostPile(BattleState state, string pile) =>
        pile switch
        {
            "hand" => (state.Hand.ToBuilder(),
                cards => state with { Hand = cards.ToImmutable() }),
            "draw" => (state.DrawPile.ToBuilder(),
                cards => state with { DrawPile = cards.ToImmutable() }),
            "discard" => (state.DiscardPile.ToBuilder(),
                cards => state with { DiscardPile = cards.ToImmutable() }),
            "exhaust" => (state.ExhaustPile.ToBuilder(),
                cards => state with { ExhaustPile = cards.ToImmutable() }),
            _ => throw new InvalidOperationException(
                $"modifyCardCost invalid Pile '{pile}', expected hand|draw|discard|exhaust"),
        };

    // ---- Phase 10.5.M2-Choose: choose detection + 候補構築 ----

    /// <summary>
    /// Choose effect で実際にプレイヤー選択 modal を出すべきかを判定。
    /// candidates &lt;= selection count の場合は auto-skip。
    /// 既存4 action の selection count は Amount、modifyCardCost は CardCount (default 1)。
    /// </summary>
    /// <param name="playingCardInstanceId">
    /// 現在プレイ中のカードの InstanceId (pause emit 時に渡す)。candidate set から除外される。
    /// 後方互換のため null default。null の場合は除外しない。
    /// </param>
    internal static bool NeedsPlayerChoice(BattleState state, CardEffect effect, DataCatalog catalog,
        string? playingCardInstanceId = null)
    {
        if (effect.Select != "choose") return false;
        if (!IsChooseSupportedAction(effect.Action)) return false;
        if (effect.Action == "modifyCardCost")
        {
            ValidateModifyCardCost(effect);
            RejectUnsupportedModifyCardCostChoose(effect);
        }
        var candidates = GetChooseCandidates(state, effect, catalog, playingCardInstanceId);
        return candidates.Length > ChooseSelectionCount(effect);
    }

    /// <summary>Choose 対応 action 一覧。</summary>
    private static bool IsChooseSupportedAction(string action)
        => action is "discard" or "exhaustCard" or "upgrade"
            or "recoverFromDiscard" or "modifyCardCost";

    /// <summary>
    /// 各 action の choose 候補 (BattleCardInstance.InstanceId 一覧) を返す。
    /// upgrade のみ「強化可能 (UpgradedCost or UpgradedEffects あり &amp;&amp; !IsUpgraded)」フィルタ適用。
    /// 候補なしのときも default ではなく ImmutableArray&lt;string&gt;.Empty を返す。
    /// </summary>
    /// <param name="playingCardInstanceId">
    /// 現在プレイ中のカードの InstanceId。候補から除外する (FinalizeCardPlay が hand から
    /// 取り出す前提で動いているため、自分自身を選ばれると確定時に Hand 不在エラーになる)。
    /// 後方互換のため null default。null の場合は除外しない。
    /// </param>
    internal static ImmutableArray<string> GetChooseCandidates(
        BattleState state, CardEffect effect, DataCatalog catalog,
        string? playingCardInstanceId = null)
    {
        if (effect.Action == "modifyCardCost")
            ValidateModifyCardCost(effect);
        var pile = ResolveChoosePile(state, effect);
        if (pile.IsDefaultOrEmpty) return ImmutableArray<string>.Empty;

        if (effect.Action == "upgrade")
        {
            var builder = ImmutableArray.CreateBuilder<string>();
            foreach (var c in pile)
            {
                // Phase 10.5.M2-Choose final review (I-1): プレイ中のカード自身を除外。
                if (c.InstanceId == playingCardInstanceId) continue;
                if (c.IsUpgraded) continue;
                if (!catalog.TryGetCard(c.CardDefinitionId, out var def)) continue;
                // Phase 10.5.M2-Choose: 既存 ApplyUpgrade と同じ「真の強化候補」フィルタを使う
                // (UpgradedKeywords のみのカードは ApplyUpgrade が rejection するため、modal にも出さない)。
                if (def.UpgradedCost is null && def.UpgradedEffects is null) continue;
                builder.Add(c.InstanceId);
            }
            return builder.ToImmutable();
        }
        // discard / exhaustCard / recoverFromDiscard / modifyCardCost は
        // pile 全カード (プレイ中のカードを除く)
        var nonUpgradeBuilder = ImmutableArray.CreateBuilder<string>();
        foreach (var c in pile)
        {
            // Phase 10.5.M2-Choose final review (I-1): プレイ中のカード自身を除外。
            if (c.InstanceId == playingCardInstanceId) continue;
            nonUpgradeBuilder.Add(c.InstanceId);
        }
        return nonUpgradeBuilder.ToImmutable();
    }

    private static ImmutableArray<BattleCardInstance> ResolveChoosePile(
        BattleState state, CardEffect effect)
    {
        // discard と recoverFromDiscard は pile 固定
        if (effect.Action == "discard") return state.Hand;
        if (effect.Action == "recoverFromDiscard") return state.DiscardPile;
        if (effect.Action == "modifyCardCost")
        {
            return effect.Pile switch
            {
                "hand" => state.Hand,
                "draw" => state.DrawPile,
                "discard" => state.DiscardPile,
                "exhaust" => state.ExhaustPile,
                _ => throw new InvalidOperationException(
                    $"modifyCardCost invalid Pile '{effect.Pile}', " +
                    "expected hand|draw|discard|exhaust"),
            };
        }
        // exhaustCard / upgrade は effect.Pile 指定 (null/empty/"hand" → hand)
        return (effect.Pile ?? "hand") switch
        {
            "" or "hand" => state.Hand,
            "draw" => state.DrawPile,
            "discard" => state.DiscardPile,
            _ => ImmutableArray<BattleCardInstance>.Empty,
        };
    }

    /// <summary>Pause 時の PendingChoice 情報を構築する (Pile を文字列で正規化)。</summary>
    /// <param name="playingCardInstanceId">
    /// 現在プレイ中のカードの InstanceId。候補から除外される (詳細は GetChooseCandidates)。
    /// 後方互換のため null default。
    /// </param>
    internal static PendingChoice BuildPendingChoice(BattleState state, CardEffect effect, DataCatalog catalog,
        string? playingCardInstanceId = null)
    {
        if (effect.Action == "modifyCardCost")
        {
            ValidateModifyCardCost(effect);
            RejectUnsupportedModifyCardCostChoose(effect);
        }
        string pile = effect.Action switch
        {
            "discard" => "hand",
            "recoverFromDiscard" => "discard",
            _ => string.IsNullOrEmpty(effect.Pile) ? "hand" : effect.Pile!,
        };
        return new PendingChoice(
            Action: effect.Action,
            Pile: pile,
            Count: ChooseSelectionCount(effect),
            CandidateInstanceIds: GetChooseCandidates(state, effect, catalog, playingCardInstanceId));
    }

    // ---- Phase 10.5.M2-Choose T4: ApplyChoseEffect (resume 経路) ----

    /// <summary>
    /// Phase 10.5.M2-Choose: 選択済 instance ids で choose effect を適用する (resume 経路)。
    /// 既存4 actions と modifyCardCost に対応。
    /// random / all モード用 helpers と同じ event 形式を踏襲する。
    /// </summary>
    internal static (BattleState, IReadOnlyList<BattleEvent>) ApplyChoseEffect(
        BattleState state, CombatActor caster, CardEffect effect,
        ImmutableArray<string> selectedInstanceIds,
        IRng rng, DataCatalog catalog,
        CombatSource? source = null,
        string? playingCardInstanceId = null)
    {
        return effect.Action switch
        {
            "discard" => ApplyDiscardChosen(
                state, caster, selectedInstanceIds, rng, catalog,
                playingCardInstanceId),
            "exhaustCard" => ApplyExhaustCardChosen(
                state, caster, effect, selectedInstanceIds, rng, catalog,
                playingCardInstanceId),
            "upgrade" => ApplyUpgradeChosen(state, caster, effect, selectedInstanceIds, catalog),
            "recoverFromDiscard" => ApplyRecoverFromDiscardChosen(state, caster, effect, selectedInstanceIds),
            "modifyCardCost" => ApplyModifyCardCostChosen(
                state, caster, effect, selectedInstanceIds, source),
            _ => throw new InvalidOperationException(
                $"ApplyChoseEffect: action '{effect.Action}' does not support choose"),
        };
    }

    /// <summary>
    /// 選択済 ids で hand から discard へ。Note: "choose"。OnCardDiscarded 発火。
    /// </summary>
    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyDiscardChosen(
        BattleState state, CombatActor caster,
        ImmutableArray<string> selectedInstanceIds,
        IRng rng, DataCatalog catalog, string? playingCardInstanceId)
    {
        if (state.Hand.Length == 0 || selectedInstanceIds.Length == 0)
            return (state, Array.Empty<BattleEvent>());

        var handBuilder = state.Hand.ToBuilder();
        var discardBuilder = state.DiscardPile.ToBuilder();
        int moved = 0;
        foreach (var id in selectedInstanceIds)
        {
            int idx = -1;
            for (int i = 0; i < handBuilder.Count; i++)
            {
                if (handBuilder[i].InstanceId == id) { idx = i; break; }
            }
            if (idx < 0) continue;  // defensive: 想定外 (validate-time に弾かれているはず)
            var card = handBuilder[idx];
            handBuilder.RemoveAt(idx);
            discardBuilder.Add(card);
            moved++;
        }

        if (moved == 0) return (state, Array.Empty<BattleEvent>());

        return BuildResult(
            state, caster, handBuilder, discardBuilder, moved, "choose", rng,
            catalog, playingCardInstanceId);
    }

    /// <summary>
    /// 選択済 ids で effect.Pile (hand/discard/draw) から exhaust へ。
    /// Note: effect.Pile (既存 ApplyExhaustCard と同じ)。OnCardExhausted 発火。
    /// </summary>
    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyExhaustCardChosen(
        BattleState state, CombatActor caster, CardEffect effect,
        ImmutableArray<string> selectedInstanceIds,
        IRng rng, DataCatalog catalog, string? playingCardInstanceId)
    {
        var (sourceBuilder, exhaustBuilder, applyResult) = OpenPile(state, effect.Pile);

        int moved = 0;
        foreach (var id in selectedInstanceIds)
        {
            int idx = -1;
            for (int i = 0; i < sourceBuilder.Count; i++)
            {
                if (sourceBuilder[i].InstanceId == id) { idx = i; break; }
            }
            if (idx < 0) continue;  // defensive
            var card = sourceBuilder[idx];
            sourceBuilder.RemoveAt(idx);
            exhaustBuilder.Add(card);
            moved++;
        }

        if (moved == 0) return (state, Array.Empty<BattleEvent>());

        var next = applyResult(sourceBuilder, exhaustBuilder);
        var events = new List<BattleEvent>
        {
            new BattleEvent(
                BattleEventKind.Exhaust, Order: 0,
                CasterInstanceId: caster.InstanceId,
                Amount: moved, Note: effect.Pile),
        };
        return FireOnCardExhausted(
            next, events, rng, catalog, playingCardInstanceId);
    }

    /// <summary>
    /// 選択済 ids で effect.Pile (hand/discard/draw) のカードを強化。
    /// Note: effect.Pile (既存 ApplyUpgrade と同じ)。trigger fire なし。
    /// 強化可能フィルタは既存 ApplyUpgrade / GetChooseCandidates と同じ条件
    /// (UpgradedCost or UpgradedEffects あり) を使用するため、validate-time で弾かれた
    /// 候補のみが届く想定。defensive な skip 分岐は残しているが通常経路では発火しない。
    /// </summary>
    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyUpgradeChosen(
        BattleState state, CombatActor caster, CardEffect effect,
        ImmutableArray<string> selectedInstanceIds,
        DataCatalog catalog)
    {
        var (sourceBuilder, applyResult) = OpenSourcePile(state, effect.Pile);

        int upgradedCount = 0;
        foreach (var id in selectedInstanceIds)
        {
            int idx = -1;
            for (int i = 0; i < sourceBuilder.Count; i++)
            {
                if (sourceBuilder[i].InstanceId == id) { idx = i; break; }
            }
            if (idx < 0) continue;  // defensive
            var card = sourceBuilder[idx];
            // 既存 ApplyUpgrade と同じ「強化可能」判定
            if (card.IsUpgraded) continue;
            if (!catalog.TryGetCard(card.CardDefinitionId, out var def)) continue;
            if (def.UpgradedCost is null && def.UpgradedEffects is null) continue;
            sourceBuilder[idx] = card with { IsUpgraded = true };
            upgradedCount++;
        }

        if (upgradedCount == 0) return (state, Array.Empty<BattleEvent>());

        var next = applyResult(sourceBuilder);
        var ev = new BattleEvent(
            BattleEventKind.Upgrade, Order: 0,
            CasterInstanceId: caster.InstanceId,
            Amount: upgradedCount, Note: effect.Pile);
        return (next, new[] { ev });
    }

    /// <summary>
    /// 選択済 ids で discard pile から effect.Pile (hand/exhaust) へ移動。
    /// Note: $"choose:{effect.Pile}" (既存 ApplyRecoverFromDiscard 形式に合わせる)。
    /// hand 上限超過時は discard に戻す (AddInstanceToPile と同じ動き)。
    /// </summary>
    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyRecoverFromDiscardChosen(
        BattleState state, CombatActor caster, CardEffect effect,
        ImmutableArray<string> selectedInstanceIds)
    {
        if (effect.Pile != "hand" && effect.Pile != "exhaust")
            throw new InvalidOperationException(
                $"recoverFromDiscard requires Pile='hand' or 'exhaust', got '{effect.Pile}'");

        if (state.DiscardPile.Length == 0 || selectedInstanceIds.Length == 0)
            return (state, Array.Empty<BattleEvent>());

        var discardBuilder = state.DiscardPile.ToBuilder();
        var picked = new List<BattleCardInstance>();
        foreach (var id in selectedInstanceIds)
        {
            int idx = -1;
            for (int i = 0; i < discardBuilder.Count; i++)
            {
                if (discardBuilder[i].InstanceId == id) { idx = i; break; }
            }
            if (idx < 0) continue;  // defensive
            picked.Add(discardBuilder[idx]);
            discardBuilder.RemoveAt(idx);
        }

        if (picked.Count == 0) return (state, Array.Empty<BattleEvent>());

        var s = state with { DiscardPile = discardBuilder.ToImmutable() };
        foreach (var card in picked)
        {
            s = AddInstanceToPile(s, card, effect.Pile!);
        }

        var ev = new BattleEvent(
            BattleEventKind.RecoverFromDiscard, Order: 0,
            CasterInstanceId: caster.InstanceId,
            Amount: picked.Count,
            Note: $"choose:{effect.Pile}");
        return (s, new[] { ev });
    }
}
