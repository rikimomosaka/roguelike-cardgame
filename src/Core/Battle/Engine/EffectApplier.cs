using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;   // 10.2.D: UnitDefinition
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
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
        string? playingCardInstanceId = null,
        CardPoolClass? sourcePoolClass = null)                // 10.2.D
    {
        // 10.5.D: AmountSource を runtime 値で resolve。null 指定の effect は素通し。
        // currentCost is intentionally evaluated once per selected card.  A
        // skill such as "make every card cost 0" must snapshot each card's
        // current cost, rather than resolving one shared Amount up front.
        var resolved = effect.Action == "modifyCardCost" &&
                       string.Equals(effect.AmountSource, "currentCost", StringComparison.Ordinal)
            ? effect
            : ResolveAmount(effect, state, caster);

        return resolved.Action switch
        {
            "attack" => ApplyAttack(state, caster, resolved, source, causeChain),
            "block"  => ApplyBlock(state, caster, resolved, rng),
            "buff"   => ApplyStatusChange(state, caster, resolved, rng, catalog, source),
            "debuff" => ApplyStatusChange(state, caster, resolved, rng, catalog, source),
            "heal"   => ApplyHeal(state, caster, resolved, rng),
            "draw"   => ApplyDraw(state, caster, resolved, rng, catalog),
            // Why: 既存 reward_*.json の draw 効果カードは "drawCards" 記法で
            // 書かれており、potion 側 "draw" と表記揺れしていた。両方サポート
            // することで洞察 / 叡智の奔流 / 戦術的撤退 等の効果が反映される。
            "drawCards" => ApplyDraw(state, caster, resolved, rng, catalog),
            "discard"=> ApplyDiscard(
                state, caster, resolved, rng, catalog,
                playingCardInstanceId),
            "exhaustSelf" => ApplyExhaustSelf(
                state, caster, rng, catalog, playingCardInstanceId),
            "retainSelf"  => (state, Array.Empty<BattleEvent>()),
            "gainEnergy"  => ApplyGainEnergy(state, caster, resolved, rng),
            "gainComboGauge" => ApplyGainComboGauge(state, caster, resolved, rng),
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
            "addCard"           => ApplyAddCard(
                state, caster, resolved, rng, catalog, playingCardInstanceId,
                ResolveSourcePoolClass(
                    state, catalog, source, playingCardInstanceId,
                    sourcePoolClass)),
            "recoverFromDiscard"=> ApplyRecoverFromDiscard(state, caster, resolved, rng, catalog),
            "recoverFromExhaust"=> ApplyRecoverFromExhaust(state, caster, resolved, rng, catalog),
            "gainMaxEnergy"     => ApplyGainMaxEnergy(state, caster, resolved, rng),
            "modifyCardCost"    => ApplyModifyCardCost(
                state, caster, resolved, rng, catalog, source,
                playingCardInstanceId),
            "setPhase"          => ApplySetPhase(state, caster, resolved, rng),
            "invertPhase"       => ApplyInvertPhase(state, caster, resolved, rng),
            "augmentCards"      => ApplyAugmentCards(
                state, caster, resolved, rng, catalog, playingCardInstanceId),
            _        => (state, Array.Empty<BattleEvent>()),
        };
    }

    /// <summary>
    /// 10.5.D: effect.AmountSource が non-null なら AmountSourceEvaluator で
    /// runtime 値を計算し、新 Amount を持った effect を返す。null は素通し。
    /// </summary>
    /// <summary>
    /// 敵カード計画用: 使用時点で確定する AmountSource だけを解決した effect を返す
    /// （既存仕様どおり AmountSource は使用時固定。能力値参照は実行時に別途評価する）。
    /// </summary>
    internal static CardEffect ResolveAmountAtUse(
        CardEffect effect, BattleState state, CombatActor caster)
    {
        // 能力値参照は予約時に固定しない（実行時の有効値を使う）。
        if (AmountSourceEvaluator.IsActorStatSource(effect.AmountSource))
            return effect;
        // 使用時に確定した値は実行時に再評価しない（実行時の root は敵の資源ではない）。
        var resolved = ResolveAmount(effect, state, caster);
        return string.IsNullOrEmpty(effect.AmountSource)
            ? resolved
            : resolved with { AmountSource = null };
    }

    /// <summary>敵の予約実行時・予告投影時に、能力値参照 AmountSource を有効値で確定させる。</summary>
    internal static CardEffect ResolveActorStatAtExecution(
        CardEffect effect, BattleState state, CombatActor caster)
        => AmountSourceEvaluator.IsActorStatSource(effect.AmountSource)
            ? ResolveAmount(effect, state, caster) with { AmountSource = null }
            : effect;

    private static CardEffect ResolveAmount(
        CardEffect effect, BattleState state, CombatActor caster)
    {
        if (string.IsNullOrEmpty(effect.AmountSource)) return effect;
        int evaluated = AmountSourceEvaluator.Evaluate(effect.AmountSource!, state, caster);
        return effect with { Amount = AmountSourceEvaluator.Scale(evaluated, effect.AmountScalePercent) };
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
            state, caster.InstanceId, events, rng, catalog,
            playingCardInstanceId);
    }

    /// <summary>
    /// Phase 10.5.L1.5: discard/exhaust の末尾で OnCardDiscarded / OnCardExhausted の
    /// Relic + Power を fire する共通ヘルパ。base events に新規 events を append し、
    /// state を更新して返す。
    /// </summary>
    private static (BattleState, IReadOnlyList<BattleEvent>) FireOnCardDiscarded(
        BattleState state, string casterActorInstanceId,
        List<BattleEvent> baseEvents, IRng rng,
        DataCatalog catalog, string? playingCardInstanceId)
        => FireTrigger(
            state, casterActorInstanceId, baseEvents,
            "OnCardDiscarded", rng, catalog,
            playingCardInstanceId);

    private static (BattleState, IReadOnlyList<BattleEvent>) FireOnCardExhausted(
        BattleState state, string casterActorInstanceId,
        List<BattleEvent> baseEvents, IRng rng,
        DataCatalog catalog, string? playingCardInstanceId)
        => FireTrigger(
            state, casterActorInstanceId, baseEvents,
            "OnCardExhausted", rng, catalog,
            playingCardInstanceId);

    private static (BattleState, IReadOnlyList<BattleEvent>) FireTrigger(
        BattleState state, string casterActorInstanceId,
        List<BattleEvent> baseEvents, string trigger,
        IRng rng, DataCatalog catalog, string? playingCardInstanceId)
    {
        var s = state;
        var (afterRelic, evsRelic) = RelicTriggerProcessor.FireFromProjectedOwner(
            s, casterActorInstanceId, trigger, catalog, rng,
            orderStart: baseEvents.Count,
            playingCardInstanceId: playingCardInstanceId);
        s = afterRelic;
        baseEvents.AddRange(evsRelic);
        var (afterPower, evsPower) = PowerTriggerProcessor.FireFromProjectedOwner(
            s, casterActorInstanceId, trigger, catalog, rng,
            orderStart: baseEvents.Count,
            playingCardInstanceId: playingCardInstanceId);
        s = afterPower;
        baseEvents.AddRange(evsPower);
        return (s, baseEvents);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyGainEnergy(
        BattleState state, CombatActor caster, CardEffect effect, IRng rng)
    {
        var targets = ResolveParticipantResourceTargets(
            state, caster, effect, rng, "gainEnergy");
        var events = new List<BattleEvent>();
        var next = state;
        foreach (var target in targets)
        {
            next = ApplyToParticipantResources(
                next, caster, target,
                projected => projected with
                {
                    Energy = EnergyMath.AddSaturating(
                        projected.Energy, effect.Amount),
                });
            events.Add(new BattleEvent(
                BattleEventKind.GainEnergy,
                Order: events.Count,
                CasterInstanceId: caster.InstanceId,
                TargetInstanceId: target.InstanceId,
                Amount: effect.Amount));
        }
        return (next, events);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyGainComboGauge(
        BattleState state, CombatActor caster, CardEffect effect, IRng rng)
    {
        var targets = ResolveParticipantResourceTargets(
            state, caster, effect, rng, "gainComboGauge");
        var events = new List<BattleEvent>();
        var next = state;
        foreach (var target in targets)
        {
            int actualChange = 0;
            next = ApplyToParticipantResources(
                next, caster, target,
                projected =>
                {
                    int before = Math.Clamp(
                        projected.ComboGauge, 0, BattleEngine.MaxComboGauge);
                    int after = (int)Math.Clamp(
                        (long)before + effect.Amount,
                        0L,
                        BattleEngine.MaxComboGauge);
                    actualChange = after - before;
                    return projected with { ComboGauge = after };
                });
            if (actualChange == 0)
                continue;
            events.Add(new BattleEvent(
                BattleEventKind.ComboGaugeChanged,
                Order: events.Count,
                CasterInstanceId: caster.InstanceId,
                TargetInstanceId: target.InstanceId,
                Amount: actualChange,
                Note: $"remaining={ResolveParticipantComboGauge(next, target)}"));
        }
        return (next, events);
    }

    private static int ResolveParticipantComboGauge(
        BattleState state,
        CombatActor target)
    {
        if (state.Participants.IsDefaultOrEmpty)
            return state.ComboGauge;
        return BattleParticipantAccess.TryGetByActor(
                state, target.InstanceId, out var participant)
            ? participant.ComboGauge
            : 0;
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
        bool revived = BattleStatusRules.TryRevive(updated, out var afterRevive);
        updated = afterRevive;
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
        if (revived)
        {
            events.Add(new BattleEvent(
                BattleEventKind.RemoveStatus, Order: order++,
                TargetInstanceId: caster.InstanceId,
                Note: BattleStatusIds.Revive,
                Source: current.GetStatusSource(BattleStatusIds.Revive)));
            events.Add(new BattleEvent(
                BattleEventKind.Heal, Order: order++,
                TargetInstanceId: caster.InstanceId,
                Amount: updated.CurrentHp,
                Note: BattleStatusIds.Revive));
        }
        else if (current.IsAlive && !updated.IsAlive)
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
        else if (updated.IsAlive && actualHpLoss > 0)
        {
            var (afterPower, evsPower) =
                PowerTriggerProcessor.FireOnDamageReceivedFromProjectedOwner(
                next, caster.InstanceId, catalog, rng, orderStart: order,
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
        BattleState state, CombatActor caster, CardEffect effect, IRng rng,
        DataCatalog catalog, string? playingCardInstanceId,
        CardPoolClass sourcePoolClass)
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
            var allocation = AllocateBattleCardInstanceId(
                s, effect.CardRefId!, playingCardInstanceId);
            s = allocation.State;
            var instance = new BattleCardInstance(
                InstanceId: allocation.InstanceId,
                CardDefinitionId: effect.CardRefId!,
                IsUpgraded: false,
                CostOverride: null)
            {
                PoolClassOverride = sourcePoolClass,
            };
            s = AddInstanceToPile(
                s, instance, effect.Pile!, rng, caster, catalog);
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
    /// Resolves the pool of the effect that is creating a card. Effect-only
    /// executions supply their immutable snapshot pool explicitly because the
    /// original card may no longer exist in any pile. Ordinary card and power
    /// effects resolve their concrete source instance across every owner zone,
    /// then fall back to the source definition. Non-card/unknown sources
    /// intentionally generate Universal cards.
    /// </summary>
    private static CardPoolClass ResolveSourcePoolClass(
        BattleState state,
        DataCatalog catalog,
        CombatSource? source,
        string? playingCardInstanceId,
        CardPoolClass? explicitPoolClass)
    {
        if (explicitPoolClass is { } explicitPool)
            return explicitPool;

        if (source is not null)
        {
            if (source.Kind is not (
                    CombatSourceKind.CardEffect
                    or CombatSourceKind.PowerEffect))
                return CardPoolClass.Universal;

            if (TryResolveCardInstancePoolClass(
                    state, catalog, source.InstanceId, out var instancePool))
                return instancePool;

            return !string.IsNullOrEmpty(source.DefinitionId)
                && catalog.TryGetCard(source.DefinitionId, out var sourceDefinition)
                ? sourceDefinition.PoolClass ?? CardPoolClass.Universal
                : CardPoolClass.Universal;
        }

        return TryResolveCardInstancePoolClass(
                state, catalog, playingCardInstanceId, out var playingPool)
            ? playingPool
            : CardPoolClass.Universal;
    }

    private static bool TryResolveCardInstancePoolClass(
        BattleState state,
        DataCatalog catalog,
        string? instanceId,
        out CardPoolClass poolClass)
    {
        if (string.IsNullOrEmpty(instanceId))
        {
            poolClass = default;
            return false;
        }

        foreach (var card in EnumerateBattleCards(state))
        {
            if (!string.Equals(
                    card.InstanceId, instanceId, StringComparison.Ordinal))
                continue;

            catalog.TryGetCard(card.CardDefinitionId, out var definition);
            poolClass = card.EffectivePoolClass(definition);
            return true;
        }

        poolClass = default;
        return false;
    }

    private static IEnumerable<BattleCardInstance> EnumerateBattleCards(
        BattleState state)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var card in EnumerateRootBattleCards(state).Concat(
                     BattleParticipantAccess.EffectiveParticipants(state)
                         .SelectMany(EnumerateParticipantBattleCards)))
        {
            if (seen.Add(card.InstanceId))
                yield return card;
        }
    }

    private static IEnumerable<BattleCardInstance> EnumerateRootBattleCards(
        BattleState state) => state.DrawPile
        .Concat(state.Hand)
        .Concat(state.DiscardPile)
        .Concat(state.ExhaustPile)
        .Concat(state.SummonHeld)
        .Concat(state.PowerCards);

    private static IEnumerable<BattleCardInstance> EnumerateParticipantBattleCards(
        BattleParticipantState participant) => participant.DrawPile
        .Concat(participant.Hand)
        .Concat(participant.DiscardPile)
        .Concat(participant.ExhaustPile)
        .Concat(participant.SummonHeld)
        .Concat(participant.PowerCards);

    /// <summary>
    /// 10.5.F: discard pile から N 枚を hand or exhaust に移動。
    /// Select=all/random 対応。choose は NotImplementedException (10.5.M で実装)。
    /// hand overflow は discard に戻す。
    /// </summary>
    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyRecoverFromDiscard(
        BattleState state, CombatActor caster, CardEffect effect, IRng rng,
        DataCatalog catalog)
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
            s = AddInstanceToPile(
                s, card, effect.Pile!, rng, caster, catalog);
        }

        var ev = new BattleEvent(
            BattleEventKind.RecoverFromDiscard, Order: 0,
            CasterInstanceId: caster.InstanceId,
            Amount: picked.Count,
            Note: $"{select}:{effect.Pile}");
        return (s, new[] { ev });
    }

    /// <summary>
    /// Moves ordinary cards from Exhaust to another ordinary pile. Armed
    /// Contingencies participate in the same selection pool, but leaving
    /// Exhaust disarms them before they enter the destination. Returning the
    /// same instance to Exhaust later therefore never arms it implicitly.
    /// </summary>
    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyRecoverFromExhaust(
        BattleState state, CombatActor caster, CardEffect effect, IRng rng,
        DataCatalog catalog)
    {
        ValidateRecoverFromExhaustDestination(effect.Pile);

        string select = effect.Select ?? "random";
        var source = state.ExhaustPile.ToBuilder();
        var picked = new List<BattleCardInstance>();

        if (select == "all")
        {
            picked.AddRange(source);
            source.Clear();
        }
        else if (select == "choose")
        {
            int target = Math.Min(effect.Amount, source.Count);
            for (int i = 0; i < target; i++)
            {
                picked.Add(source[0]);
                source.RemoveAt(0);
            }
        }
        else if (select == "random")
        {
            int target = Math.Min(effect.Amount, source.Count);
            for (int i = 0; i < target; i++)
            {
                int index = rng.NextInt(0, source.Count);
                picked.Add(source[index]);
                source.RemoveAt(index);
            }
        }
        else
        {
            throw new InvalidOperationException(
                $"recoverFromExhaust invalid Select '{effect.Select}', expected all|random|choose");
        }

        return CompleteRecoverFromExhaust(
            state, caster, effect, select, source, picked, rng, catalog);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) CompleteRecoverFromExhaust(
        BattleState state,
        CombatActor caster,
        CardEffect effect,
        string select,
        ImmutableArray<BattleCardInstance>.Builder remainingExhaust,
        IReadOnlyList<BattleCardInstance> picked,
        IRng rng,
        DataCatalog catalog)
    {
        if (picked.Count == 0)
            return (state, Array.Empty<BattleEvent>());

        // Prune at the source-removal boundary, not after the destination is
        // populated. This preserves the "leave once = disarmed" invariant even
        // when a later effect in the same resolution returns the card to Exhaust.
        var s = BattleEngine.PruneArmedContingencies(state with
        {
            ExhaustPile = remainingExhaust.ToImmutable(),
        });
        foreach (var card in picked)
        {
            s = AddInstanceToPile(
                s, card, effect.Pile!, rng, caster, catalog);
        }

        return (s, new[]
        {
            new BattleEvent(
                BattleEventKind.RecoverFromExhaust,
                Order: 0,
                CasterInstanceId: caster.InstanceId,
                Amount: picked.Count,
                Note: $"exhaust:{select}:{effect.Pile}"),
        });
    }

    private static void ValidateRecoverFromExhaustDestination(string? pile)
    {
        if (pile is not ("hand" or "draw" or "discard"))
            throw new InvalidOperationException(
                $"recoverFromExhaust requires Pile='hand', 'draw', or 'discard', got '{pile}'");
    }

    /// <summary>
    /// 10.5.F: ターン開始時の Energy 追加量（互換名 EnergyMax）を永続的に増やす。
    /// 当ターンの Energy はそのままで、次ターン以降の持越し加算へ反映する。
    /// </summary>
    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyGainMaxEnergy(
        BattleState state, CombatActor caster, CardEffect effect, IRng rng)
    {
        var targets = ResolveParticipantResourceTargets(
            state, caster, effect, rng, "gainMaxEnergy");
        var events = new List<BattleEvent>();
        var next = state;
        foreach (var target in targets)
        {
            next = ApplyToParticipantResources(
                next, caster, target,
                projected => projected with
                {
                    EnergyMax = EnergyMath.AddSaturating(
                        projected.EnergyMax, effect.Amount),
                });
            events.Add(new BattleEvent(
                BattleEventKind.GainMaxEnergy,
                Order: events.Count,
                CasterInstanceId: caster.InstanceId,
                TargetInstanceId: target.InstanceId,
                Amount: effect.Amount));
        }
        return (next, events);
    }

    /// <summary>
    /// 10.5.F: pile 名から BattleCardInstance を追加した state を返す。
    /// hand 上限超過時は discard に流す。draw は top (先頭) に挿入。
    /// </summary>
    private static BattleState AddInstanceToPile(
        BattleState state, BattleCardInstance instance, string pile, IRng rng,
        CombatActor owner, DataCatalog catalog) => pile switch
    {
        "hand" => state.Hand.Length < ResolveHandLimit(state, owner, catalog)
            ? state with { Hand = state.Hand.Add(
                BattleStatusRules.ApplyConfusionOnHandEntry(
                    state, instance, rng, owner)) }
            : state with { DiscardPile = state.DiscardPile.Add(instance) },
        "draw"    => state with { DrawPile = state.DrawPile.Insert(0, instance) },
        "discard" => state with { DiscardPile = state.DiscardPile.Add(instance) },
        "exhaust" => state with { ExhaustPile = state.ExhaustPile.Add(instance) },
        _ => throw new InvalidOperationException(
            $"Unknown pile '{pile}', expected hand|draw|discard|exhaust"),
    };

    /// <summary>
    /// Allocates a replay-stable, battle-wide card id without consuming RNG.
    /// The monotonic ordinal is persisted in BattleState; the collision scan
    /// also makes migrated or hand-crafted states safe when it starts at zero.
    /// </summary>
    private static (BattleState State, string InstanceId)
        AllocateBattleCardInstanceId(
            BattleState state,
            string cardRefId,
            string? playingCardInstanceId)
    {
        long ordinal = Math.Max(0L, state.NextGeneratedCardOrdinal);
        while (ordinal < long.MaxValue)
        {
            string candidate = $"{cardRefId}-generated-{ordinal}";
            if (!ContainsCardInstanceId(
                    state, candidate, playingCardInstanceId))
            {
                return (
                    state with { NextGeneratedCardOrdinal = ordinal + 1L },
                    candidate);
            }

            ordinal++;
        }

        throw new InvalidOperationException(
            "The generated battle-card instance id sequence is exhausted.");
    }

    private static bool ContainsCardInstanceId(
        BattleState state,
        string candidate,
        string? playingCardInstanceId)
    {
        if (string.Equals(
                candidate,
                playingCardInstanceId,
                StringComparison.Ordinal))
        {
            return true;
        }

        static bool Contains(
            ImmutableArray<BattleCardInstance> cards,
            string instanceId) => cards.Any(card => string.Equals(
                card.InstanceId, instanceId, StringComparison.Ordinal));

        if (Contains(state.DrawPile, candidate)
            || Contains(state.Hand, candidate)
            || Contains(state.DiscardPile, candidate)
            || Contains(state.ExhaustPile, candidate)
            || Contains(state.SummonHeld, candidate)
            || Contains(state.PowerCards, candidate))
        {
            return true;
        }

        if (state.Participants.IsDefaultOrEmpty)
            return false;

        foreach (var participant in state.Participants)
        {
            if (Contains(participant.DrawPile, candidate)
                || Contains(participant.Hand, candidate)
                || Contains(participant.DiscardPile, candidate)
                || Contains(participant.ExhaustPile, candidate)
                || Contains(participant.SummonHeld, candidate)
                || Contains(participant.PowerCards, candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyAttack(
        BattleState state, CombatActor caster, CardEffect effect, CombatSource? source,
        DamageCauseChain? causeChain)
    {
        // stale ref 対策: state から InstanceId で最新の actor を再取得する
        var current = FindActor(state, caster.InstanceId) ?? caster;
        var hit = new AttackHit(
            effect.Amount,
            source ?? CombatSource.Unattributed,
            causeChain ?? DamageCauseChain.Empty)
        {
            // PlayCard projects the acting participant into the legacy root
            // fields before effects run. Capture that command-time selection
            // here so later target changes cannot redirect an earlier card.
            TargetInstanceId = effect.Scope == EffectScope.Single
                ? GetSelectedEnemyInstanceId(state)
                : null,
        };
        var updated = effect.Scope switch
        {
            EffectScope.Single => current with { AttackSingle = current.AttackSingle.Add(hit) },
            EffectScope.Random => current with { AttackRandom = current.AttackRandom.Add(hit) },
            EffectScope.All    => current with { AttackAll    = current.AttackAll.Add(hit) },
            _ => current, // Self は CardEffect.Normalize で弾かれる想定
        };
        var next = ReplaceActor(state, caster.InstanceId, updated);
        if (effect.Scope is not (EffectScope.Single or EffectScope.Random or EffectScope.All))
            return (next, Array.Empty<BattleEvent>());

        return (next, new[]
        {
            new BattleEvent(
                BattleEventKind.GainAttack,
                Order: 0,
                CasterInstanceId: current.InstanceId,
                TargetInstanceId: current.InstanceId,
                Amount: effect.Amount,
                Note: effect.Scope.ToString().ToLowerInvariant(),
                Source: source,
                CauseChain: causeChain),
        });
    }

    private static string? GetSelectedEnemyInstanceId(BattleState state)
    {
        if (state.TargetEnemyIndex is not { } index
            || index < 0
            || index >= state.Enemies.Length)
            return null;

        return state.Enemies[index].InstanceId;
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyBlock(
        BattleState state, CombatActor caster, CardEffect effect, IRng rng)
    {
        // Why: 防御カードを「味方単体対象 (含 hero)」でも撃てるようにするため、
        // scope=Self 以外 (Single+Ally / All+Ally など) も ResolveTargets 経由で
        // 受ける。stale ref 対策で各 target を InstanceId で再取得する。
        IReadOnlyList<CombatActor> targets = effect.Scope == EffectScope.Self
            ? new[] { caster }
            : ResolveTargets(state, caster, effect, rng);
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
        DataCatalog catalog, CombatSource? source)
    {
        var changes = effect.EffectiveStatusChanges();
        if (changes.Count == 0)
            throw new InvalidOperationException(
                "buff/debuff effect requires name/amount or a non-empty statusChanges array");

        var targets = ResolveTargets(state, caster, effect, rng);
        if (targets.Count == 0)
            return (state, Array.Empty<BattleEvent>());

        var events = new List<BattleEvent>();
        int order = 0;
        var s = state;
        var currentCaster = FindActor(state, caster.InstanceId) ?? caster;
        // Resonance follows the resolved target set, not the authored scope.
        // A Single ally buff explicitly aimed at the caster is a self-only
        // application; a buff aimed at somebody else or at multiple actors is
        // not.  This also keeps Random/All deterministic after target choice.
        bool mayTriggerResonance = effect.Action == "buff"
            && targets.Count == 1
            && string.Equals(
                targets[0].InstanceId,
                caster.InstanceId,
                StringComparison.Ordinal)
            && currentCaster.GetStatus(BattleStatusIds.Resonance) > 0;
        var resonanceChanges = new List<StatusChange>();

        // InstanceId snapshot で各 target を更新
        var targetIds = targets.Select(t => t.InstanceId).ToList();
        foreach (var tid in targetIds)
        {
            // 最新 state から再 fetch
            CombatActor? current = FindActor(s, tid);
            if (current is null) continue;

            // A grouped debuff is one application. Resistance therefore blocks the
            // complete group and spends one stack instead of applying only part of it.
            bool isDebuff = changes.Any(change => IsHarmfulStatusChange(effect.Action, change));
            int resistance = current.GetStatus(BattleStatusIds.Resistance);
            if (isDebuff && resistance > 0)
            {
                var resisted = BattleStatusRules.Decrement(
                    current, BattleStatusIds.Resistance);
                s = ReplaceActor(s, tid, resisted);
                events.Add(new BattleEvent(
                    resistance == 1
                        ? BattleEventKind.RemoveStatus
                        : BattleEventKind.ApplyStatus,
                    Order: order++,
                    CasterInstanceId: caster.InstanceId,
                    TargetInstanceId: tid,
                    Amount: -1,
                    Note: BattleStatusIds.Resistance,
                    Source: current.GetStatusSource(BattleStatusIds.Resistance)));
                continue;
            }

            foreach (var change in changes)
            {
                current = FindActor(s, tid);
                if (current is null) break;

                int currentAmount = current.GetStatus(change.Name);
                bool isPositiveAfterglow =
                    string.Equals(
                        change.Name,
                        BattleStatusIds.Afterglow,
                        StringComparison.Ordinal)
                    && change.Amount > 0;
                int newAmount = isPositiveAfterglow
                    ? 1
                    : (int)Math.Clamp(
                        (long)currentAmount + change.Amount,
                        int.MinValue,
                        int.MaxValue);

                // Phase 10.6.B フォローアップ: strength / dexterity は signed (負値も保持)。
                // weak / vulnerable / poison 等の ailment は従来どおり 0 以下で remove。
                bool isSigned = SignedStatuses.Contains(change.Name);
                ImmutableDictionary<string, int> newStatuses;
                if (isSigned)
                {
                    // signed: 0 のみ remove、それ以外 (正/負) は保持
                    newStatuses = newAmount == 0
                        ? current.Statuses.Remove(change.Name)
                        : current.Statuses.SetItem(change.Name, newAmount);
                }
                else
                {
                    // unsigned: 0 以下で remove (既存挙動)
                    newStatuses = newAmount <= 0
                        ? current.Statuses.Remove(change.Name)
                        : current.Statuses.SetItem(change.Name, newAmount);
                }

                var statusSources = current.StatusSources;
                bool statusRemoved = isSigned ? newAmount == 0 : newAmount <= 0;
                var statusDisplayOrder = BattleStatusRules.NormalizeStatusDisplayOrder(current);
                if (statusRemoved)
                {
                    statusSources = statusSources.Remove(change.Name);
                    statusDisplayOrder = statusDisplayOrder.Remove(change.Name);
                }
                else if (change.Amount > 0)
                {
                    // The most recent positive application owns future status ticks.
                    // A null producer deliberately replaces any stale owner with
                    // Unattributed instead of crediting the previous applier.
                    statusSources = statusSources.SetItem(
                        change.Name, source ?? CombatSource.Unattributed);
                    if (currentAmount == 0)
                        statusDisplayOrder = statusDisplayOrder.Remove(change.Name).Add(change.Name);
                }

                var updated = current with
                {
                    Statuses = newStatuses,
                    StatusSources = statusSources,
                    StatusDisplayOrder = statusDisplayOrder,
                };
                s = ReplaceActor(s, tid, updated);

                // signed 系は値変化時に常に ApplyStatus 発火 (負値含む)
                // unsigned 系は newAmount > 0 のときのみ ApplyStatus
                bool didApply = isPositiveAfterglow
                    ? currentAmount <= 0
                    : isSigned
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
                        Amount: isPositiveAfterglow
                            ? Math.Max(0, newAmount - currentAmount)
                            : change.Amount,
                        Note: change.Name,
                        Source: source ?? CombatSource.Unattributed));

                    if (mayTriggerResonance
                        && string.Equals(tid, caster.InstanceId, StringComparison.Ordinal)
                        && change.Amount > 0
                        && !string.Equals(
                            change.Name,
                            BattleStatusIds.Resonance,
                            StringComparison.Ordinal)
                        && StatusDefinition.Get(change.Name).Kind == StatusKind.Buff)
                    {
                        resonanceChanges.Add(isPositiveAfterglow
                            ? change with { Amount = 1 }
                            : change);
                    }
                }
                else if (didRemove)
                {
                    events.Add(new BattleEvent(
                        BattleEventKind.RemoveStatus, Order: order++,
                        TargetInstanceId: tid,
                        Note: change.Name,
                        Source: source ?? CombatSource.Unattributed));
                }
            }
        }

        if (resonanceChanges.Count > 0)
            s = PositiveBuffApplicationRules.ApplyResonance(
                s,
                caster.InstanceId,
                currentCaster.GetStatus(BattleStatusIds.Resonance),
                resonanceChanges,
                source,
                events,
                ref order,
                catalog,
                rng);

        return (s, events);
    }

    private static bool IsHarmfulStatusChange(string action, StatusChange change)
    {
        var statusDefinition = StatusDefinition.Get(change.Name);
        return statusDefinition.Kind == StatusKind.Debuff && change.Amount > 0
            || action == "debuff" && SignedStatuses.Contains(change.Name) && change.Amount < 0;
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

    /// <summary>
    /// Card resources belong to battle participants, not to the shared battle
    /// root. Self targets the acting participant; Single/Random/All may target
    /// another allied participant through the normal actor targeting rules.
    /// </summary>
    private static IReadOnlyList<CombatActor> ResolveParticipantResourceTargets(
        BattleState state,
        CombatActor caster,
        CardEffect effect,
        IRng rng,
        string action)
    {
        if (effect.Scope != EffectScope.Self && effect.Side != EffectSide.Ally)
            throw new InvalidOperationException(
                $"{action} requires Side=Ally for scope {effect.Scope}, got {effect.Side}");

        IReadOnlyList<CombatActor> targets;
        if (effect.Scope is EffectScope.Random or EffectScope.All)
        {
            // Resource effects can only be applied to actors backed by a
            // BattleParticipant. A legacy fixed-move summon may share the
            // allied actor pool, but it has no hand/energy/card-cost state and
            // therefore must not enter an All result or a Random draw.
            var candidates = ResolveSidePool(state, caster, effect.Side!.Value)
                .Where(target => target.IsAlive
                    && OwnsParticipantResources(state, target))
                .ToList();
            if (effect.Scope == EffectScope.All)
            {
                targets = candidates;
            }
            else if (candidates.Count == 0)
            {
                targets = Array.Empty<CombatActor>();
            }
            else
            {
                targets = new[] { candidates[rng.NextInt(0, candidates.Count)] };
            }
        }
        else
        {
            // Preserve explicit Single semantics: selecting an actor that has
            // no participant remains an authoring/command error rather than a
            // silent no-op.
            targets = ResolveTargets(state, caster, effect, rng);
        }

        foreach (var target in targets)
        {
            if (!OwnsParticipantResources(state, target))
                throw new InvalidOperationException(
                    $"{action} target '{target.InstanceId}' does not own card resources");
        }
        return targets;
    }

    private static bool OwnsParticipantResources(
        BattleState state,
        CombatActor target) => target.Side == ActorSide.Enemy
            ? EnemyCardPlanner.TryGetParticipant(state, target.InstanceId, out _)
            : !state.Participants.IsDefaultOrEmpty
            ? BattleParticipantAccess.TryGetByActor(
                state, target.InstanceId, out _)
            : ProvisionalSoloHeroOwnerResolver.Resolve(state)?.Hero.InstanceId
                == target.InstanceId;

    /// <summary>
    /// Mutates one participant's projected card-resource root, preserving the
    /// acting participant's in-flight changes when the target is somebody else.
    /// The returned root is always restored to the acting participant so the
    /// surrounding PlayCard/trigger command can continue normally.
    /// </summary>
    private static BattleState ApplyToParticipantResources(
        BattleState state,
        CombatActor caster,
        CombatActor target,
        Func<BattleState, BattleState> mutate)
    {
        // 敵カード計画中は敵参加者が root へ投影されている。敵の資源効果は
        // 自分自身の手札・山札・エナジーにだけ作用する。
        if (caster.Side == ActorSide.Enemy || target.Side == ActorSide.Enemy)
        {
            if (!string.Equals(caster.InstanceId, target.InstanceId, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Enemy card resource effects can only target the caster itself ('{caster.InstanceId}' -> '{target.InstanceId}').");
            return mutate(state);
        }

        if (state.Participants.IsDefaultOrEmpty)
        {
            var legacyOwner = ProvisionalSoloHeroOwnerResolver.Resolve(state)?.Hero;
            if (legacyOwner is null || legacyOwner.InstanceId != target.InstanceId)
                throw new InvalidOperationException(
                    $"Actor '{target.InstanceId}' does not own legacy card resources");
            return mutate(state);
        }

        if (!BattleParticipantAccess.TryGetByActor(
                state, caster.InstanceId, out var actingParticipant))
            throw new InvalidOperationException(
                $"Caster '{caster.InstanceId}' does not own participant resources");
        if (!BattleParticipantAccess.TryGetByActor(
                state, target.InstanceId, out var targetParticipant))
            throw new InvalidOperationException(
                $"Target '{target.InstanceId}' does not own participant resources");

        if (actingParticipant.ParticipantId == targetParticipant.ParticipantId)
            return mutate(state);

        var committedActor = BattleParticipantAccess.CommitRootToParticipant(
            state, actingParticipant.ParticipantId);
        var projectedTarget = BattleParticipantAccess.ProjectToRoot(
            committedActor, targetParticipant.ParticipantId);
        projectedTarget = mutate(projectedTarget);
        var committedTarget = BattleParticipantAccess.CommitRootToParticipant(
            projectedTarget, targetParticipant.ParticipantId);
        return BattleParticipantAccess.ProjectToRoot(
            committedTarget, actingParticipant.ParticipantId);
    }

    private static int ResolveHandLimit(
        BattleState state,
        CombatActor owner,
        DataCatalog catalog)
    {
        string definitionId = owner.DefinitionId;
        if (owner.Side == ActorSide.Enemy)
            return catalog.TryGetEnemy(definitionId, out var enemy) && enemy.IsCardControlled
                ? enemy.HandLimit
                : DrawHelper.HandCap;
        if (BattleParticipantAccess.TryGetByActor(
                state, owner.InstanceId, out var participant))
            definitionId = participant.CharacterId;
        return catalog.TryGetUnit(definitionId, out var unit)
            ? unit.HandLimit
            : DrawHelper.HandCap;
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
        BattleState state, CombatActor caster, CardEffect effect, IRng rng,
        DataCatalog catalog)
    {
        var targets = ResolveParticipantResourceTargets(
            state, caster, effect, rng, "draw");
        var events = new List<BattleEvent>();
        var next = state;
        foreach (var target in targets)
        {
            int actualDrawn = 0;
            int shuffledCardCount = 0;
            next = ApplyToParticipantResources(
                next, caster, target,
                projected => DrawHelper.Draw(
                    projected,
                    effect.Amount,
                    rng,
                    ResolveHandLimit(projected, target, catalog),
                    FindActor(projected, target.InstanceId) ?? target,
                    out actualDrawn,
                    out shuffledCardCount));
            if (shuffledCardCount > 0)
            {
                events.Add(new BattleEvent(
                    BattleEventKind.Shuffle,
                    Order: events.Count,
                    CasterInstanceId: caster.InstanceId,
                    TargetInstanceId: target.InstanceId,
                    Amount: shuffledCardCount));
            }
            if (actualDrawn == 0) continue;
            events.Add(new BattleEvent(
                BattleEventKind.Draw,
                Order: events.Count,
                CasterInstanceId: caster.InstanceId,
                TargetInstanceId: target.InstanceId,
                Amount: actualDrawn));
        }
        return events.Count == 0
            ? (state, Array.Empty<BattleEvent>())
            : (next, events);
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
            next, caster.InstanceId, events, rng, catalog,
            playingCardInstanceId);
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
            next, caster.InstanceId, events, rng, catalog,
            excludedHandInstanceId);
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

        // Pile validation and selection are centralized in OpenSourcePile.
        var (sourceBuilder, applyResult) = OpenSourcePile(state, effect.Pile);

        // 強化候補抽出: IsUpgraded=false かつ definition.IsUpgradable
        var candidates = new List<int>();
        for (int i = 0; i < sourceBuilder.Count; i++)
        {
            var card = sourceBuilder[i];
            if (IsHandPile(effect.Pile)
                && card.InstanceId == excludedHandInstanceId)
            {
                continue;
            }
            if (card.IsUpgraded) continue;
            if (!catalog.TryGetCard(card.CardDefinitionId, out var def)) continue;
            if (!def.IsUpgradable) continue;
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
            "exhaust" => (state.ExhaustPile.ToBuilder(),
                s => state with { ExhaustPile = s.ToImmutable() }),
            null => throw new InvalidOperationException("upgrade requires Pile (hand|discard|draw|exhaust)"),
            _ => throw new InvalidOperationException($"upgrade invalid Pile '{pileName}', expected hand|discard|draw|exhaust"),
        };
    }

    private static bool IsHandPile(string? pileName) =>
        string.IsNullOrEmpty(pileName)
        || string.Equals(pileName, "hand", StringComparison.Ordinal);

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

        string instanceIdBase =
            $"summon_inst_{state.Turn}_{rng.NextInt(0, 1 << 30):x}";
        string newInstanceId = instanceIdBase;
        int collisionOrdinal = 1;
        while (state.Allies.Any(actor => actor.InstanceId == newInstanceId)
               || state.Enemies.Any(actor => actor.InstanceId == newInstanceId))
        {
            newInstanceId = $"{instanceIdBase}_{collisionOrdinal++}";
        }
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
            // Card-controlled allies own a participant/deck and do not use
            // the legacy fixed-move or summon-lifetime systems.
            CurrentMoveId: unitDef.IsCardControlled ? null : unitDef.InitialMoveId,
            // Allied summons no longer have an authoring/runtime lifetime.
            // RemainingLifetimeTurns stays on old snapshots only so existing
            // saves can still be read and cleaned up safely.
            RemainingLifetimeTurns: null,
            AssociatedSummonHeldInstanceId: null)    // PlayCard card-move logic で設定
        {
            AttackPower = unitDef.Attack,
            DefensePower = unitDef.Defense,
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

    // ---- setPhase / invertPhase（位相） ----

    private static (BattleState, IReadOnlyList<BattleEvent>) ApplySetPhase(
        BattleState state, CombatActor caster, CardEffect effect, IRng rng)
    {
        var next = PhaseRules.Parse(effect.Name
            ?? throw new InvalidOperationException("setPhase requires Name (none|calm|motion)"));
        var events = new List<BattleEvent>();
        int order = 0;
        foreach (var target in ResolveTargets(state, caster, effect, rng))
            state = PhaseRules.SetPhase(state, target.InstanceId, next, "effect", ref order, events);
        return (state, events);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyInvertPhase(
        BattleState state, CombatActor caster, CardEffect effect, IRng rng)
    {
        var events = new List<BattleEvent>();
        int order = 0;
        foreach (var target in ResolveTargets(state, caster, effect, rng))
        {
            var current = FindActor(state, target.InstanceId) ?? target;
            state = PhaseRules.SetPhase(
                state, current.InstanceId, PhaseRules.Invert(current.Phase), "effect", ref order, events);
        }
        return (state, events);
    }

    // ---- augmentCards（カードの後付け能力） ----

    /// <summary>
    /// 使用者のカードにキーワードを足す。Name = キーワード ID、Amount = 足す数、
    /// Pile = hand|draw|discard|exhaust|self、Select = all|random|choose、CardTypeFilter = 種別の絞り込み。
    /// 戦闘中の手札・山札は使用者の参加者ぶんが root に投影済みなので、Scope は Self のみ。
    /// </summary>
    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyAugmentCards(
        BattleState state, CombatActor caster, CardEffect effect, IRng rng,
        DataCatalog catalog, string? playingCardInstanceId)
    {
        ValidateAugmentCards(effect);
        string pile = effect.Pile ?? "hand";
        if (pile == "self")
        {
            if (playingCardInstanceId is null) return (state, Array.Empty<BattleEvent>());
            return AugmentSelected(state, caster, effect, FindPileOf(state, playingCardInstanceId),
                new[] { playingCardInstanceId });
        }

        var candidates = AugmentCandidateIds(AugmentPile(state, pile), effect, catalog, playingCardInstanceId);
        int count = effect.CardCount ?? 1;
        IReadOnlyList<string> selected = effect.Select switch
        {
            "all" => candidates,
            "random" => PickRandomCardInstanceIds(candidates, count, rng),
            // 候補が選択数以下なら選ぶ必要が無い（それ以外は PlayCard 側が選択待ちにする）。
            "choose" when candidates.Count <= count => candidates,
            "choose" => throw new InvalidOperationException(
                "augmentCards select='choose' requires a PendingCardPlay choice"),
            _ => throw new InvalidOperationException(
                $"augmentCards invalid Select '{effect.Select}', expected all|random|choose"),
        };
        return AugmentSelected(state, caster, effect, pile, selected);
    }

    private static void ValidateAugmentCards(CardEffect effect)
    {
        if (effect.Scope != EffectScope.Self)
            throw new InvalidOperationException("augmentCards only supports Scope=Self");
        if (string.IsNullOrEmpty(effect.Name) || CardKeywords.Get(effect.Name) is null)
            throw new InvalidOperationException($"augmentCards requires a known keyword Name, got '{effect.Name}'");
        if (effect.Amount <= 0)
            throw new InvalidOperationException($"augmentCards Amount must be positive, got {effect.Amount}");
    }

    private static ImmutableArray<BattleCardInstance> AugmentPile(BattleState state, string? pile) =>
        (pile ?? "hand") switch
        {
            "hand" => state.Hand,
            "draw" => state.DrawPile,
            "discard" => state.DiscardPile,
            "exhaust" => state.ExhaustPile,
            _ => throw new InvalidOperationException(
                $"augmentCards invalid Pile '{pile}', expected hand|draw|discard|exhaust|self"),
        };

    private static List<string> AugmentCandidateIds(
        ImmutableArray<BattleCardInstance> pile, CardEffect effect, DataCatalog catalog,
        string? playingCardInstanceId)
    {
        CardType? filter = string.IsNullOrEmpty(effect.CardTypeFilter)
            ? null
            : Enum.Parse<CardType>(effect.CardTypeFilter!, ignoreCase: true);
        var ids = new List<string>(pile.Length);
        foreach (var card in pile)
        {
            if (card.InstanceId == playingCardInstanceId) continue;
            if (filter is { } type
                && (!catalog.TryGetCard(card.CardDefinitionId, out var def) || def.CardType != type))
                continue;
            ids.Add(card.InstanceId);
        }
        return ids;
    }

    private static string FindPileOf(BattleState state, string instanceId)
    {
        if (state.Hand.Any(c => c.InstanceId == instanceId)) return "hand";
        if (state.DrawPile.Any(c => c.InstanceId == instanceId)) return "draw";
        if (state.DiscardPile.Any(c => c.InstanceId == instanceId)) return "discard";
        return "exhaust";
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) AugmentSelected(
        BattleState state, CombatActor caster, CardEffect effect, string pile,
        IReadOnlyList<string> selectedInstanceIds)
    {
        if (selectedInstanceIds.Count == 0) return (state, Array.Empty<BattleEvent>());
        var builder = AugmentPile(state, pile).ToBuilder();
        var events = new List<BattleEvent>();
        foreach (var id in selectedInstanceIds.Distinct(StringComparer.Ordinal))
        {
            int index = -1;
            for (int i = 0; i < builder.Count; i++)
                if (builder[i].InstanceId == id) { index = i; break; }
            if (index < 0) continue;
            var before = builder[index];
            var augmented = before.AddKeyword(effect.Name!, effect.Amount);
            int added = augmented.KeywordCount(effect.Name!) - before.KeywordCount(effect.Name!);
            if (added <= 0) continue;
            builder[index] = augmented;
            events.Add(new BattleEvent(
                BattleEventKind.CardAugmented, events.Count,
                CasterInstanceId: caster.InstanceId,
                TargetInstanceId: id,
                Amount: added,
                Note: $"{effect.Name}:{augmented.KeywordCount(effect.Name!)}"));
        }
        var cards = builder.ToImmutable();
        var next = pile switch
        {
            "hand" => state with { Hand = cards },
            "draw" => state with { DrawPile = cards },
            "discard" => state with { DiscardPile = cards },
            _ => state with { ExhaustPile = cards },
        };
        return (next, events);
    }

    // ---- modifyCardCost ----

    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyModifyCardCost(
        BattleState state, CombatActor caster, CardEffect effect, IRng rng,
        DataCatalog catalog, CombatSource? source, string? excludedHandInstanceId)
    {
        var duration = ValidateModifyCardCost(effect);
        RejectUnsupportedModifyCardCostChoose(effect, source);
        var targets = ResolveParticipantResourceTargets(
            state, caster, effect, rng, "modifyCardCost");
        var allEvents = new List<BattleEvent>();
        var next = state;
        foreach (var target in targets)
        {
            IReadOnlyList<BattleEvent> targetEvents = Array.Empty<BattleEvent>();
            next = ApplyToParticipantResources(
                next, caster, target,
                projected =>
                {
                    var currentTarget = FindActor(projected, target.InstanceId)
                        ?? target;
                    var (after, events) = ApplyModifyCardCostToProjectedTarget(
                        projected,
                        caster.InstanceId,
                        currentTarget,
                        effect,
                        duration,
                        rng,
                        catalog,
                        source,
                        target.InstanceId == caster.InstanceId
                            ? excludedHandInstanceId
                            : null);
                    targetEvents = events;
                    return after;
                });
            foreach (var battleEvent in targetEvents)
                allEvents.Add(battleEvent with { Order = allEvents.Count });
        }
        return (next, allEvents);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>)
        ApplyModifyCardCostToProjectedTarget(
            BattleState state,
            string eventCasterInstanceId,
            CombatActor costOwner,
            CardEffect effect,
            CardCostModifierDuration duration,
            IRng rng,
            DataCatalog catalog,
            CombatSource? source,
            string? excludedHandInstanceId)
    {
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
            state, eventCasterInstanceId, costOwner, effect, duration,
            pileBuilder, applyResult,
            selectedIds, catalog, source);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyModifyCardCostChosen(
        BattleState state, CombatActor caster, CardEffect effect,
        ImmutableArray<string> selectedInstanceIds, DataCatalog catalog,
        CombatSource? source)
    {
        var duration = ValidateModifyCardCost(effect);
        RejectUnsupportedModifyCardCostChoose(effect, source);
        if (effect.Select != "choose")
            throw new InvalidOperationException(
                $"modifyCardCost chosen path requires Select='choose', got '{effect.Select}'");
        var (pileBuilder, applyResult) = OpenCardCostPile(state, effect.Pile!);
        return ApplyCardCostModifierToSelected(
            state, caster.InstanceId, caster, effect, duration,
            pileBuilder, applyResult,
            selectedInstanceIds, catalog, source);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyCardCostModifierToSelected(
        BattleState state,
        string eventCasterInstanceId,
        CombatActor costOwner,
        CardEffect effect,
        CardCostModifierDuration duration,
        ImmutableArray<BattleCardInstance>.Builder pileBuilder,
        Func<ImmutableArray<BattleCardInstance>.Builder, BattleState> applyResult,
        IReadOnlyList<string> selectedInstanceIds,
        DataCatalog? catalog,
        CombatSource? source)
    {
        if (selectedInstanceIds.Count == 0)
            return (state, Array.Empty<BattleEvent>());

        var events = new List<BattleEvent>();
        var appliedIds = new HashSet<string>(StringComparer.Ordinal);
        string? sourceId = source?.InstanceId
            ?? source?.DefinitionId
            ?? source?.ActorInstanceId;
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

            var selectedCard = pileBuilder[index];
            int modifierAmount = effect.Amount;
            if (string.Equals(effect.AmountSource, "currentCost", StringComparison.Ordinal))
            {
                if (catalog is null || !catalog.TryGetCard(selectedCard.CardDefinitionId, out var definition))
                    throw new InvalidOperationException(
                        $"card '{selectedCard.CardDefinitionId}' not in catalog");
                long? currentCost = BattleCardCostCalculator.GetRawCostBeforeCombo(
                    selectedCard, definition, costOwner,
                    isInHand: effect.Pile == "hand");
                if (currentCost is null or <= 0) continue;
                modifierAmount = (int)Math.Clamp(-currentCost.Value, int.MinValue, int.MaxValue);
            }
            if (modifierAmount == 0) continue;
            pileBuilder[index] = selectedCard.AddCostModifier(
                new CardCostModifier(modifierAmount, duration, sourceId));
            events.Add(new BattleEvent(
                BattleEventKind.ModifyCardCost,
                Order: events.Count,
                CasterInstanceId: eventCasterInstanceId,
                TargetInstanceId: instanceId,
                Amount: modifierAmount,
                Note: note,
                Source: source));
        }

        if (events.Count == 0)
            return (state, Array.Empty<BattleEvent>());
        return (applyResult(pileBuilder), events);
    }

    private static CardCostModifierDuration ValidateModifyCardCost(CardEffect effect)
    {
        if (effect.Scope != EffectScope.Self && effect.Side != EffectSide.Ally)
            throw new InvalidOperationException(
                $"modifyCardCost requires Side=Ally for scope {effect.Scope}, got {effect.Side}");
        bool resolvesFromCurrentCost = string.Equals(
            effect.AmountSource, "currentCost", StringComparison.Ordinal);
        if (effect.Amount == 0 && !resolvesFromCurrentCost)
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
        if (effect.Scope != EffectScope.Self)
            throw new InvalidOperationException(
                "modifyCardCost select='choose' only supports Scope=Self");
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
        effect.Action is "modifyCardCost" or "augmentCards"
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
    /// Card-selection actions use Amount; modifyCardCost uses CardCount (default 1).
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
            or "recoverFromDiscard" or "recoverFromExhaust"
            or "modifyCardCost" or "augmentCards";

    /// <summary>
    /// 各 action の choose 候補 (BattleCardInstance.InstanceId 一覧) を返す。
    /// upgrade のみ「CardDefinition.IsUpgradable &amp;&amp; !IsUpgraded」フィルタ適用。
    /// 候補なしのときも default ではなく ImmutableArray&lt;string&gt;.Empty を返す。
    /// </summary>
    /// <param name="playingCardInstanceId">
    /// 現在プレイ中のカードの InstanceId。手札を選択元とする場合だけ候補から除外する。
    /// 伏策のように実カードが除外へ存在する効果実行では、同じInstanceIdも通常候補になる。
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

        if (effect.Action == "augmentCards")
            return AugmentCandidateIds(pile, effect, catalog, playingCardInstanceId).ToImmutableArray();

        if (effect.Action == "upgrade")
        {
            var builder = ImmutableArray.CreateBuilder<string>();
            foreach (var c in pile)
            {
                // Phase 10.5.M2-Choose final review (I-1): プレイ中のカード自身を除外。
                if (ShouldExcludeInFlightHandCard(
                        effect,
                        c.InstanceId,
                        playingCardInstanceId))
                {
                    continue;
                }
                if (c.IsUpgraded) continue;
                if (!catalog.TryGetCard(c.CardDefinitionId, out var def)) continue;
                // CardDefinition.IsUpgradable is the single source of truth. Keyword-only
                // and usage-limit-only upgrades are valid candidates too.
                if (!def.IsUpgradable) continue;
                builder.Add(c.InstanceId);
            }
            return builder.ToImmutable();
        }
        // Non-upgrade actions use every card in their source pile. Only an
        // in-flight card still occupying Hand needs the safety exclusion.
        var nonUpgradeBuilder = ImmutableArray.CreateBuilder<string>();
        foreach (var c in pile)
        {
            // Phase 10.5.M2-Choose final review (I-1): プレイ中のカード自身を除外。
            if (ShouldExcludeInFlightHandCard(
                    effect,
                    c.InstanceId,
                    playingCardInstanceId))
            {
                continue;
            }
            nonUpgradeBuilder.Add(c.InstanceId);
        }
        return nonUpgradeBuilder.ToImmutable();
    }

    private static bool ShouldExcludeInFlightHandCard(
        CardEffect effect,
        string candidateInstanceId,
        string? playingCardInstanceId)
    {
        if (!string.Equals(
                candidateInstanceId,
                playingCardInstanceId,
                StringComparison.Ordinal))
        {
            return false;
        }

        return effect.Action switch
        {
            "discard" => true,
            "exhaustCard" or "upgrade" => IsHandPile(effect.Pile),
            "modifyCardCost" => string.Equals(
                effect.Pile,
                "hand",
                StringComparison.Ordinal),
            _ => false,
        };
    }

    private static ImmutableArray<BattleCardInstance> ResolveChoosePile(
        BattleState state, CardEffect effect)
    {
        // discard と recoverFromDiscard は pile 固定
        if (effect.Action == "discard") return state.Hand;
        if (effect.Action == "recoverFromDiscard") return state.DiscardPile;
        if (effect.Action == "recoverFromExhaust")
        {
            ValidateRecoverFromExhaustDestination(effect.Pile);
            return state.ExhaustPile;
        }
        if (effect.Action == "augmentCards")
            return AugmentPile(state, effect.Pile);
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
            "exhaust" when effect.Action == "upgrade" => state.ExhaustPile,
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
            "recoverFromExhaust" => "exhaust",
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
            "recoverFromDiscard" => ApplyRecoverFromDiscardChosen(
                state, caster, effect, selectedInstanceIds, rng, catalog),
            "recoverFromExhaust" => ApplyRecoverFromExhaustChosen(
                state, caster, effect, selectedInstanceIds, rng, catalog),
            "modifyCardCost" => ApplyModifyCardCostChosen(
                state, caster, effect, selectedInstanceIds, catalog, source),
            "augmentCards" => AugmentSelected(
                state, caster, effect, effect.Pile ?? "hand", selectedInstanceIds),
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
            next, caster.InstanceId, events, rng, catalog,
            playingCardInstanceId);
    }

    /// <summary>
    /// 選択済 ids で effect.Pile (hand/discard/draw) のカードを強化。
    /// Note: effect.Pile (既存 ApplyUpgrade と同じ)。trigger fire なし。
    /// 強化可能フィルタは既存 ApplyUpgrade / GetChooseCandidates と同じ
    /// CardDefinition.IsUpgradable を使用するため、validate-time で弾かれた
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
            if (!def.IsUpgradable) continue;
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
        ImmutableArray<string> selectedInstanceIds, IRng rng,
        DataCatalog catalog)
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
            s = AddInstanceToPile(
                s, card, effect.Pile!, rng, caster, catalog);
        }

        var ev = new BattleEvent(
            BattleEventKind.RecoverFromDiscard, Order: 0,
            CasterInstanceId: caster.InstanceId,
            Amount: picked.Count,
            Note: $"choose:{effect.Pile}");
        return (s, new[] { ev });
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) ApplyRecoverFromExhaustChosen(
        BattleState state, CombatActor caster, CardEffect effect,
        ImmutableArray<string> selectedInstanceIds, IRng rng,
        DataCatalog catalog)
    {
        ValidateRecoverFromExhaustDestination(effect.Pile);
        if (state.ExhaustPile.IsDefaultOrEmpty
            || selectedInstanceIds.IsDefaultOrEmpty)
        {
            return (state, Array.Empty<BattleEvent>());
        }

        var remaining = state.ExhaustPile.ToBuilder();
        var picked = new List<BattleCardInstance>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string instanceId in selectedInstanceIds)
        {
            if (!seen.Add(instanceId)) continue;

            int index = -1;
            for (int i = 0; i < remaining.Count; i++)
            {
                if (string.Equals(
                        remaining[i].InstanceId,
                        instanceId,
                        StringComparison.Ordinal))
                {
                    index = i;
                    break;
                }
            }
            if (index < 0) continue;

            picked.Add(remaining[index]);
            remaining.RemoveAt(index);
        }

        return CompleteRecoverFromExhaust(
            state,
            caster,
            effect,
            "choose",
            remaining,
            picked,
            rng,
            catalog);
    }
}
