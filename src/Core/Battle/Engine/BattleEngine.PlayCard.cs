using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

public static partial class BattleEngine
{
    public static (BattleState, IReadOnlyList<BattleEvent>) PlayCard(
        BattleState state, int handIndex,
        int? targetEnemyIndex, int? targetAllyIndex,
        IRng rng, DataCatalog catalog)
    {
        RejectIfParticipantReady(state, participantId: null, "PlayCard");
        var (next, events) = PlayCardForActor(
            state, casterActorInstanceId: null, handIndex,
            targetEnemyIndex, targetAllyIndex, rng, catalog);
        // The legacy solo API continues to treat the root as authoritative.
        // This keeps it compatible with commands not yet participant-aware,
        // while persisting the completed primary command into Participants.
        if (!next.Participants.IsDefaultOrEmpty)
        {
            next = BattleParticipantAccess.CommitRootToParticipant(
                next, next.Participants[0].ParticipantId);
        }
        (next, events) = ApplyOutcomeAfterPlanningCommand(next, events);
        return (IncrementActionRevision(next), events);
    }

    /// <summary>
    /// Plays a card from one participant's hand. Participant resources are
    /// projected through the legacy root engine for the duration of the
    /// command, then committed back without changing the primary root mirror.
    /// </summary>
    public static (BattleState, IReadOnlyList<BattleEvent>) PlayCard(
        BattleState state, string participantId, int handIndex,
        int? targetEnemyIndex, int? targetAllyIndex,
        IRng rng, DataCatalog catalog)
    {
        if (!BattleParticipantAccess.TryGet(state, participantId, out var participant))
            throw new InvalidOperationException(
                $"Battle participant '{participantId}' does not exist.");
        RejectIfParticipantReady(state, participantId, "PlayCard");

        var projected = BattleParticipantAccess.ProjectToRoot(state, participantId);
        var (processed, events) = PlayCardForActor(
            projected, participant.ActorInstanceId, handIndex,
            targetEnemyIndex, targetAllyIndex, rng, catalog);
        var committed = BattleParticipantAccess.CommitRootToParticipant(
            processed, participantId);
        (committed, events) = ApplyOutcomeAfterPlanningCommand(
            committed, events);
        return (IncrementActionRevision(committed), events);
    }

    /// <summary>
    /// 敵カード計画用。呼び出し側（<see cref="EnemyCardPlanner"/>）が敵参加者を root へ投影済みの
    /// state を渡す。戦場効果は予約のみ行い、ActionRevision は進めない（敵の計画は
    /// ターン開始処理の一部であり独立コマンドではない）。
    /// </summary>
    internal static (BattleState, IReadOnlyList<BattleEvent>) PlanEnemyCard(
        BattleState projectedState, string enemyActorInstanceId, int handIndex,
        IRng rng, DataCatalog catalog)
        => PlayCardForActor(
            projectedState, enemyActorInstanceId, handIndex,
            targetEnemyIndex: null, targetAllyIndex: null,
            rng, catalog, planEnemyEffects: true);

    /// <param name="planEnemyEffects">
    /// true は敵カード計画（<see cref="EnemyCardPlanner"/>）。コスト・コンボ・使用回数・
    /// カード移動・手札系効果は通常どおり処理し、戦場効果は適用せず caster の
    /// <see cref="CombatActor.PlannedEffects"/> へ予約する。特性・レリック・パワー・有事は発火しない。
    /// </param>
    private static (BattleState, IReadOnlyList<BattleEvent>) PlayCardForActor(
        BattleState state, string? casterActorInstanceId, int handIndex,
        int? targetEnemyIndex, int? targetAllyIndex,
        IRng rng, DataCatalog catalog,
        bool planEnemyEffects = false)
    {
        ValidateComboTrackingState(state);
        if (state.Phase != BattlePhase.PlayerInput)
            throw new InvalidOperationException($"PlayCard requires Phase=PlayerInput, got {state.Phase}");
        if (handIndex < 0 || handIndex >= state.Hand.Length)
            throw new InvalidOperationException($"handIndex {handIndex} out of range [0, {state.Hand.Length})");

        // Phase 10.5.M2-Choose: 既存 pending を resolve しないと PlayCard 不可
        RejectWhileCardChoiceIsPending(state, "PlayCard");

        var card = state.Hand[handIndex];
        if (!catalog.TryGetCard(card.CardDefinitionId, out var def))
            throw new InvalidOperationException($"card '{card.CardDefinitionId}' not in catalog");
        CombatActor actingActor;
        if (casterActorInstanceId is null)
        {
            var owner = ProvisionalSoloHeroOwnerResolver.Resolve(state)
                ?? throw new InvalidOperationException(
                    "PlayCard requires a living hero owner");
            actingActor = owner.Hero;
        }
        else
        {
            actingActor = FindLivingParticipantCaster(
                state, casterActorInstanceId, allowEnemy: planEnemyEffects);
        }
        var effects = (card.IsUpgraded && def.UpgradedEffects is not null)
            ? def.UpgradedEffects
            : def.Effects;
        var usesPerTurn = def.EffectiveUsesPerTurn(card.IsUpgraded);
        if (CardUsageTracker.HasReachedLimit(state, def.Id, usesPerTurn))
            throw new InvalidOperationException(
                $"card '{def.Id}' has reached its per-turn usage limit");
        string? restriction = BattleCardCostCalculator.GetPlayRestrictionReason(
            state, actingActor, def);
        if (restriction is not null)
            throw new InvalidOperationException(
                $"card '{def.Id}' is restricted by status '{restriction}'");

        // コンボ参照値は常にカード定義上の元コスト。CostOverride、カード個体modifier、
        // 手札中status、奥義、コンボ軽減のいずれにも影響されない。
        int? origCost = BattleCardCostCalculator.GetOriginalCost(card, def);
        if (origCost is null)
            throw new InvalidOperationException($"card '{def.Id}' is unplayable (cost=null)");
        int actualCost = origCost.Value;
        int comboGaugeCost = def.ComboGaugeCost;
        if (comboGaugeCost < 0)
            throw new InvalidOperationException(
                $"card '{def.Id}' has an invalid combo gauge cost: {comboGaugeCost}");
        if (state.ComboGauge < comboGaugeCost)
            throw new InvalidOperationException(
                $"insufficient combo gauge: have {state.ComboGauge}, need {comboGaugeCost}");

        // Why: 強化により獲得するキーワード (例: starter_summon_3+ で superwild) を反映するため、
        // 単一 Keywords ではなく EffectiveKeywords(IsUpgraded) を参照する。
        var keywords = def.EffectiveKeywords(card.IsUpgraded);
        bool hasWild = keywords?.Contains("wild") == true;
        bool hasSuperWild = keywords?.Contains("superwild") == true;
        // SuperWild は Wild の使用済み状態に影響されず、何度でも発動する。
        // 両方の keyword を持つ場合も SuperWild を優先し、Wild 使用済みにはしない。
        bool superWildEffective = hasSuperWild;
        bool wildEffective = hasWild && !hasSuperWild && !state.WildUsedInCurrentCombo;
        bool grantsNextCardProtection = superWildEffective || wildEffective;

        bool continuesFromCarryover = state.ComboCarryoverPending;
        bool isContinuing = continuesFromCarryover
            || BattleCardCostCalculator.IsComboContinuation(
                state, card, def, actingActor);
        bool usesContinuity = BattleCardCostCalculator.UsesContinuity(
            state, card, def, actingActor);
        bool comboWasBroken = state.LastPlayedOrigCost is not null && !isContinuing;

        // 軽減は「通常の次コスト条件」または「直前の Wild 系による次カード保護」で発生する。
        // 今回の Wild / SuperWild 自身のキーワードだけで継続する場合は、そのカードを軽減しない。
        // 一方、直前の保護中なら今回のカード種別に関係なく軽減する。
        // 支払は「個体modifier → 手札中の主人公status → コンボ-1」の順。
        // 現在コストが0でも、コンボ判定と LastPlayedOrigCost は上のactualCostを使う。
        var cost = BattleCardCostCalculator.EvaluateForHand(
            state, card, def, actingActor);
        int payCost = cost.PayableCost
            ?? throw new InvalidOperationException($"card '{def.Id}' is unplayable (cost=null)");

        if (state.Energy < payCost)
            throw new InvalidOperationException($"insufficient energy: have {state.Energy}, need {payCost}");

        // 10.2.C: combo フィールド更新
        int newCombo = isContinuing
            ? (int)Math.Min(int.MaxValue, (long)state.ComboCount + 1)
            : 1;
        int newMaxCombo = Math.Max(state.MaxComboThisTurn, newCombo);
        int maxComboIncrease = (int)Math.Min(
            int.MaxValue,
            Math.Max(0L, (long)newMaxCombo - state.MaxComboThisTurn));
        int gaugeBefore = Math.Clamp(state.ComboGauge, 0, MaxComboGauge);
        int earnedGauge = actingActor.GetStatus(BattleStatusIds.Stagnation) > 0
            ? 0
            : maxComboIncrease;
        int newComboGauge = (int)Math.Min(
            MaxComboGauge, Math.Max(0L, (long)gaugeBefore + earnedGauge - comboGaugeCost));
        if (comboWasBroken && actingActor.GetStatus(BattleStatusIds.Distress) > 0)
            newComboGauge = 0;
        int comboGaugeIncrease = newComboGauge - gaugeBefore;
        int? newLastCost = actualCost;
        // Wild / SuperWild はどちらも「次の 1 枚」まで保護する。
        // 保護を消費したカードが新たに保護を予約しなければ false へ戻る。
        bool newFreePass = grantsNextCardProtection;
        // Wild だけが同一コンボ中の後続 Wild を無効化する。
        // 既に使用済みの Wild がコンボを切った場合、そのプレイでは
        // 効果を再発動させず、新しいコンボの使用済み状態だけを解除する。
        // The first Wild still activates its protection, even though that
        // first card is not itself a combo continuation.
        bool newWildUsed = wildEffective
            || (isContinuing && state.WildUsedInCurrentCombo);

        // コスト変更は領域移動時ではなく、プレイが受理された時点で消費する。
        // choose pause中も既にプレイ済みなので、Hand上の同一個体を書き換えておく。
        var playedCard = card.ConsumeCostChangesOnPlay();
        var s = state with
        {
            Energy = comboWasBroken && actingActor.GetStatus(BattleStatusIds.Distress) > 0
                ? 0
                : state.Energy - payCost,
            Hand = state.Hand.SetItem(handIndex, playedCard),
            ComboCount = newCombo,
            MaxComboThisTurn = newMaxCombo,
            ComboGauge = newComboGauge,
            LastPlayedOrigCost = newLastCost,
            NextCardComboFreePass = newFreePass,
            WildUsedInCurrentCombo = newWildUsed,
            ComboCarryoverPending = false,
            TargetEnemyIndex = targetEnemyIndex ?? state.TargetEnemyIndex,
            TargetAllyIndex = targetAllyIndex ?? state.TargetAllyIndex,
        };

        var updatedActor = FindParticipantCaster(s, actingActor.InstanceId)
            ?? throw new InvalidOperationException(
                $"Card caster '{actingActor.InstanceId}' disappeared during play.");
        if (usesContinuity)
            updatedActor = BattleStatusRules.Decrement(
                updatedActor, BattleStatusIds.Continuity);
        // 節制 / 浪費はターンを跨いで保持し、実際にカードを使用するたびに
        // そのカードの支払計算へ適用した後で各1ずつ消費する。
        updatedActor = BattleStatusRules.Decrement(
            updatedActor, BattleStatusIds.HandCostReduction);
        updatedActor = BattleStatusRules.Decrement(
            updatedActor, BattleStatusIds.HandCostIncrease);
        int curse = updatedActor.GetStatus(BattleStatusIds.Curse);
        if (curse > 0)
        {
            updatedActor = BattleStatusRules.Decrement(updatedActor, BattleStatusIds.Curse);
            if (curse == 1)
                s = s with { CardUseLockedThisTurn = true };
        }
        s = ReplaceParticipantCaster(s, updatedActor);
        s = CardUsageTracker.MarkUsed(s, def.Id, usesPerTurn);

        var playedSnapshot = CreatePlayedCardSnapshot(
            s, playedCard, def, updatedActor, effects,
            originalCost: actualCost, paidCost: payCost);
        s = RecordActualCardPlay(s, playedSnapshot, isContinuing);

        var cardSource = new CombatSource(
            CombatSourceKind.CardEffect,
            OwnerSlot: actingActor.OwnerPlayerSlot ?? actingActor.SlotIndex,
            OwnerInstanceId: actingActor.OwnerActorInstanceId ?? actingActor.InstanceId,
            ActorInstanceId: actingActor.InstanceId,
            DefinitionId: def.Id,
            InstanceId: card.InstanceId);

        var events = new List<BattleEvent>
        {
            new(BattleEventKind.PlayCard, Order: 0,
                CasterInstanceId: actingActor.InstanceId,
                CardId: def.Id,
                Amount: payCost,
                Source: cardSource,
                ComboCountAfter: newCombo),
        };

        if (comboGaugeIncrease != 0)
        {
            events.Add(new BattleEvent(
                BattleEventKind.ComboGaugeChanged,
                Order: events.Count,
                CasterInstanceId: actingActor.InstanceId,
                Amount: comboGaugeIncrease,
                CardId: def.Id,
                Note: $"remaining={newComboGauge}",
                Source: cardSource));
        }

        int order = events.Count;

        // 位相: 使用時の位相で判定する（効果の実行より前）。消えるのは効果処理の後（FinalizeCardPlay）。
        // 双極は動で使ったときにエナジーへ換わる。敵の予約（planEnemyEffects）では
        // 戦場へ反映しないので、判定は再現実行時（EnemyAttackingResolver）に行い、双極もそのまま残す。
        if (!planEnemyEffects)
        {
            var phaseActor = FindParticipantCaster(s, actingActor.InstanceId) ?? updatedActor;
            var usedPhase = phaseActor.Phase;
            // 双極は使えば位相に関係なく消える。エナジーを得るのは動で使ったときだけ。
            int bipolar = playedCard.KeywordCount(CardKeywords.Bipolar);
            if (bipolar > 0)
            {
                playedCard = playedCard.RemoveKeyword(CardKeywords.Bipolar);
                s = s with { Hand = s.Hand.SetItem(handIndex, playedCard) };
            }
            if (usedPhase == ActorPhase.Motion && bipolar > 0)
            {
                var (afterEnergy, energyEvents) = EffectApplier.Apply(
                    s, phaseActor,
                    new CardEffect("gainEnergy", EffectScope.Self, null, bipolar),
                    rng, catalog, cardSource);
                s = afterEnergy;
                foreach (var energyEvent in energyEvents)
                    events.Add(energyEvent with { Order = order++, Note = CardKeywords.Bipolar });
            }
            s = PhaseRules.MarkExitOnUse(s, actingActor.InstanceId, def.CardType);
        }

        if (planEnemyEffects)
        {
            var enemyCaster = FindParticipantCaster(s, actingActor.InstanceId)
                ?? updatedActor;
            var (afterPlan, planEvents, _, _) = ApplyEffectsFrom(
                s, enemyCaster, playedCard, effects, startIndex: 0, newCombo,
                summonedActorInstanceIdsIn: ImmutableArray<string>.Empty,
                ref order, rng, catalog, planEnemyEffects: true);
            events.AddRange(planEvents);
            if (afterPlan.PendingCardPlay is not null)
                throw new InvalidOperationException(
                    $"Enemy card '{def.Id}' requires a choice, which enemy planning does not support.");
            var moved = MovePlayedCardToDestination(
                afterPlan, playedCard, def, ImmutableArray<string>.Empty);
            return (moved, events);
        }

        // Character traits observe the maximum-combo edge after the card has
        // joined the combo, but before any of that card's effects execute.
        // The caller has already projected the acting participant into the
        // root state, so the shared trait runtime can update its resources and
        // usage counter directly here.
        var traitResult = CharacterTraitRuntime.TryTrigger(
            s,
            actingActor.InstanceId,
            CharacterTraitTrigger.OnComboReached,
            catalog,
            rng,
            previousValue: state.MaxComboThisTurn,
            currentValue: newMaxCombo,
            orderStart: order);
        s = traitResult.State;
        foreach (var battleEvent in traitResult.Events)
            events.Add(battleEvent with { Order = order++ });

        var caster = FindParticipantCaster(s, actingActor.InstanceId)
            ?? updatedActor;

        var contingency = FindAbility<ContingencyAbilityDefinition>(
            def, playedCard.IsUpgraded);
        var (afterEffects, effectEvents, summonedActorInstanceIds, _) =
            contingency is null
                ? ApplyEffectsFrom(
                    s, caster, playedCard, effects, startIndex: 0, newCombo,
                    summonedActorInstanceIdsIn: ImmutableArray<string>.Empty,
                    ref order, rng, catalog)
                : (s, new List<BattleEvent>(), ImmutableArray<string>.Empty, 0);
        s = afterEffects;
        events.AddRange(effectEvents);

        // Phase 10.5.M2-Choose: pause 中は OnPlayCard / カード移動 / Power トリガを発火せず即返却
        if (s.PendingCardPlay is not null)
            return (s, events);

        var (afterReprise, repriseEvents, reprisePaused) = ExecuteRepriseIfPresent(
            s,
            FindParticipantCaster(s, actingActor.InstanceId) ?? actingActor,
            playedCard,
            def,
            summonedActorInstanceIds,
            ref order,
            rng,
            catalog);
        s = afterReprise;
        events.AddRange(repriseEvents);
        if (reprisePaused)
            return (s, events);

        // Phase 10.5.M2-Choose T4: 終盤共通処理 (relic / カード移動 / power triggers) を切り出し。
        // 同じ helper を ResolveCardChoice からも呼ぶ。move 対象 card は handIndex ではなく
        // InstanceId で検索 (resume で hand 順序が変わっている可能性に対応)。
        return FinalizeCardPlay(
            s, actingActor.InstanceId, playedCard, def, summonedActorInstanceIds,
            events, ref order, rng, catalog);
    }

    /// <summary>
    /// Phase 10.5.M2-Choose: PlayCard / ResolveCardChoice 両方から呼ばれる、
    /// effect 適用完了後の共通処理。OnPlayCard relic 発火 → 5 段カード移動 →
    /// PowerTriggerProcessor (OnPlayCard / OnCombo) を順次実行する。
    /// 移動対象 card は handIndex ではなく InstanceId で検索する (resume 経路で
    /// hand 順序が変わっている可能性があるため)。
    /// </summary>
    private static (BattleState, IReadOnlyList<BattleEvent>) FinalizeCardPlay(
        BattleState state, string casterActorInstanceId,
        BattleCardInstance card, CardDefinition def,
        ImmutableArray<string> summonedActorInstanceIds,
        List<BattleEvent> events, ref int order,
        IRng rng, DataCatalog catalog)
    {
        var s = state;

        // 位相: 使用時の判定で消える予定になった位相を、効果処理が終わったここで消す。
        // 効果で位相を得た（反転を含む）場合は予定が取り消されているので消えない。
        s = PhaseRules.ResolvePendingExit(s, casterActorInstanceId, ref order, events);

        // Contingency is armed before OnPlayCard triggers.  Its normal effect
        // list is deliberately skipped by the caller, and this destination
        // takes precedence over both Wait and Exhaust.
        var contingency = FindAbility<ContingencyAbilityDefinition>(
            def, card.IsUpgraded);
        // Reprise itself has already completed before FinalizeCardPlay is
        // entered (including a resumed Reprise choice).  A normal Attack or
        // Skill therefore becomes eligible now.  Merely arming a Contingency
        // is not a resolved effect and is deliberately excluded here.
        if (contingency is null)
            s = RecordActualCardResolutionForReprise(s, card.InstanceId);
        if (contingency is not null)
        {
            int contingencyHandIndex = -1;
            for (int i = 0; i < s.Hand.Length; i++)
            {
                if (s.Hand[i].InstanceId == card.InstanceId)
                {
                    contingencyHandIndex = i;
                    break;
                }
            }
            if (contingencyHandIndex < 0)
                throw new InvalidOperationException(
                    $"FinalizeCardPlay: contingency card '{card.InstanceId}' not in Hand");
            s = s with
            {
                Hand = s.Hand.RemoveAt(contingencyHandIndex),
                ExhaustPile = s.ExhaustPile.Add(card),
            };
            s = ArmContingency(s, casterActorInstanceId, card, contingency);
            events.Add(new BattleEvent(
                BattleEventKind.ContingencyArmed,
                Order: order++,
                CasterInstanceId: casterActorInstanceId,
                CardId: def.Id,
                Note: contingency.Trigger.Timing.ToString()));
        }

        // 10.2.E 追加: OnPlayCard レリック発動（effect 適用後・カード移動前）
        // Phase 10.5.L1.5: trigger ID を power 側と統一 ("OnCardPlay" → "OnPlayCard")。
        var (afterRelic, evsRelic) = RelicTriggerProcessor.FireFromProjectedOwner(
            s, casterActorInstanceId, "OnPlayCard", catalog, rng,
            orderStart: order,
            playingCardInstanceId: card.InstanceId);
        s = afterRelic;
        foreach (var ev in evsRelic) { events.Add(ev with { Order = order++ }); }
        s = PruneArmedContingencies(s);

        if (contingency is null)
            s = MovePlayedCardToDestination(s, card, def, summonedActorInstanceIds);

        // 10.5.E: destination pile 振り分け後の power トリガ発動
        // OnPlayCard はカード移動完了後 (Power カード自身が PowerCards に居る状態で) 発火
        var (afterOnPlay, evsOnPlay) = PowerTriggerProcessor.FireFromProjectedOwner(
            s, casterActorInstanceId, "OnPlayCard", catalog, rng,
            orderStart: order,
            playingCardInstanceId: card.InstanceId);
        s = afterOnPlay;
        foreach (var ev in evsOnPlay) { events.Add(ev with { Order = order++ }); }
        s = PruneArmedContingencies(s);

        // OnCombo は combo update 後で fire (combo は冒頭で update 済み)
        var (afterCombo, evsCombo) = PowerTriggerProcessor.FireOnComboFromProjectedOwner(
            s, casterActorInstanceId, s.ComboCount, catalog, rng,
            orderStart: order,
            playingCardInstanceId: card.InstanceId);
        s = afterCombo;
        foreach (var ev in evsCombo) { events.Add(ev with { Order = order++ }); }

        int comboAtThreshold = s.ComboCount;
        var contingencyResolution = ResolveComboContingenciesFromProjectedOwner(
            s,
            casterActorInstanceId,
            previousCombo: Math.Max(0, comboAtThreshold - 1),
            currentCombo: comboAtThreshold,
            rng,
            catalog,
            order);
        s = contingencyResolution.State;
        foreach (var battleEvent in contingencyResolution.Events)
            events.Add(battleEvent with { Order = order++ });

        if (contingencyResolution.PausedForChoice)
        {
            if (FindAbility<FinisherAbilityDefinition>(def, card.IsUpgraded) is not null)
            {
                var pending = s.PendingCardPlay
                    ?? throw new InvalidOperationException(
                        "ComboReached Contingency paused without a pending choice.");
                s = s with
                {
                    PendingCardPlay = pending with
                    {
                        EffectExecution = pending.EffectExecution! with
                        {
                            PostContingencyFinisherCard = card,
                            PostContingencyFinisherActorInstanceId =
                                casterActorInstanceId,
                        },
                    },
                };
            }
            return (s, events);
        }

        s = ApplyFinisher(
            s, casterActorInstanceId, card, def, events, ref order);

        return (s, events);
    }

    /// <summary>
    /// Phase 10.5.M2-Choose: effect ループを resume 可能な形に切り出した内部 helper。
    /// startIndex から effects[i] を順次適用し、このカード自身が生成した
    /// summon actor instance ID を pause/resume 間でも引き継ぐ。
    /// T2 時点では behavior 変更なし、PlayCard からのみ呼ばれる。
    /// </summary>
    /// <param name="card">T2 では未使用。T3 で choose effect 検出時に PendingCardPlay.CardInstanceId として参照される。</param>
    /// <returns>(更新後 state, 発火 events, 直接生成した actor IDs, 次に処理する effect index)</returns>
    private static (
        BattleState state,
        List<BattleEvent> events,
        ImmutableArray<string> summonedActorInstanceIds,
        int nextIndex) ApplyEffectsFrom(
        BattleState state, CombatActor caster, BattleCardInstance card,
        IReadOnlyList<CardEffect> effects, int startIndex, int newCombo,
        ImmutableArray<string> summonedActorInstanceIdsIn, ref int order,
        IRng rng, DataCatalog catalog,
        bool planEnemyEffects = false)
    {
        var s = state;
        var events = new List<BattleEvent>();
        var summonedActorInstanceIds = summonedActorInstanceIdsIn.IsDefault
            ? ImmutableArray.CreateBuilder<string>()
            : summonedActorInstanceIdsIn.ToBuilder();
        for (int i = startIndex; i < effects.Count; i++)
        {
            var eff = effects[i];
            // 10.5.E: Trigger 指定 effect は即時実行ではなく、PowerTriggerProcessor 経由で対応
            if (!string.IsNullOrEmpty(eff.Trigger)) continue;

            // 10.2.C: per-effect comboMin filter（PlayCard 経路のみ）
            if (eff.ComboMin is { } min && newCombo < min) continue;
            if (eff.MaxComboMin is { } maxMin
                && s.MaxComboThisTurn < maxMin)
                continue;

            // Phase 10.5.M2-Choose: choose effect で pause が必要なら early return
            // I-1 fix: プレイ中のカード自身を candidate から除外するため card.InstanceId を渡す。
            if (EffectApplier.NeedsPlayerChoice(s, eff, catalog, card.InstanceId))
            {
                var pending = new PendingCardPlay(
                    CardInstanceId: card.InstanceId,
                    EffectIndex: i,
                    SummonedActorInstanceIdsBefore: summonedActorInstanceIds.ToImmutable(),
                    Choice: EffectApplier.BuildPendingChoice(s, eff, catalog, card.InstanceId));
                var paused = s with { PendingCardPlay = pending };
                return (paused, events, summonedActorInstanceIds.ToImmutable(), i);
            }

            var source = new CombatSource(
                CombatSourceKind.CardEffect,
                OwnerSlot: caster.OwnerPlayerSlot ?? caster.SlotIndex,
                OwnerInstanceId: caster.OwnerActorInstanceId ?? caster.InstanceId,
                ActorInstanceId: caster.InstanceId,
                DefinitionId: card.CardDefinitionId,
                InstanceId: card.InstanceId,
                EffectIndex: i,
                Scope: eff.Scope.ToString().ToLowerInvariant());
            if (planEnemyEffects && EnemyCardPlanner.IsDeferredBattlefieldAction(eff.Action))
            {
                // 敵の戦場効果は予測のみ。使用時に確定する値（AmountSource）だけ解決して予約する。
                var plannedEffect = EffectApplier.ResolveAmountAtUse(eff, s, caster);
                var planned = caster with
                {
                    PlannedEffects = caster.PlannedEffects.Add(
                        new PlannedCardEffect(plannedEffect, source)),
                };
                s = ReplaceParticipantCaster(s, planned);
                caster = planned;
                continue;
            }
            var (afterEffect, evs) = EffectApplier.Apply(
                s, caster, eff, rng, catalog, source,
                playingCardInstanceId: card.InstanceId);
            s = afterEffect;
            s = PruneArmedContingencies(s);
            foreach (var ev in evs)
            {
                if (eff.Action == "summon"
                    && ev.Kind == BattleEventKind.Summon
                    && ev.TargetInstanceId is { } summonedId
                    && !summonedActorInstanceIds.Contains(summonedId))
                {
                    summonedActorInstanceIds.Add(summonedId);
                }
                events.Add(ev with { Order = order });
                order++;
            }
            caster = FindParticipantCaster(s, caster.InstanceId) ?? caster;
        }
        return (s, events, summonedActorInstanceIds.ToImmutable(), effects.Count);
    }

    /// <summary>
    /// Binds one Unit card to the exact actor instances created by that card.
    /// Multiple direct summons form one ownership group and share the same held
    /// card id; the card returns only after the final group member leaves play.
    /// </summary>
    private static bool TryBindSummonedActors(
        BattleState state,
        string cardInstanceId,
        ImmutableArray<string> summonedActorInstanceIds,
        out BattleState result)
    {
        result = state;
        if (summonedActorInstanceIds.IsDefaultOrEmpty)
            return false;

        var summonedIds = summonedActorInstanceIds.ToHashSet(StringComparer.Ordinal);
        bool boundAny = false;
        var allies = state.Allies.ToBuilder();
        for (int index = 0; index < allies.Count; index++)
        {
            var actor = allies[index];
            if (!summonedIds.Contains(actor.InstanceId))
                continue;
            if (actor.DefinitionId == "hero")
                throw new InvalidOperationException(
                    $"Unit card '{cardInstanceId}' cannot own hero actor '{actor.InstanceId}'.");
            if (actor.AssociatedSummonHeldInstanceId is not null
                && !string.Equals(
                    actor.AssociatedSummonHeldInstanceId,
                    cardInstanceId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Summoned actor '{actor.InstanceId}' is already associated with " +
                    $"card '{actor.AssociatedSummonHeldInstanceId}'.");
            }

            allies[index] = actor with
            {
                AssociatedSummonHeldInstanceId = cardInstanceId,
            };
            boundAny = true;
        }

        if (boundAny)
            result = state with { Allies = allies.ToImmutable() };
        return boundAny;
    }

    /// <summary>
    /// 使用済みカードを手札から行き先へ移す。
    /// 10.2.D: 5 段優先順位（exhaustSelf → Power → Unit+success → retainSelf → Discard）
    /// 10.5.M2/M3: retainSelf / exhaustSelf は keyword "wait" / "exhaust" で代替可能化。
    /// 後方互換のため action と keyword の両方をチェック。
    /// プレイヤー・CPU味方・敵カード計画で共通利用する。
    /// </summary>
    private static BattleState MovePlayedCardToDestination(
        BattleState state,
        BattleCardInstance card,
        CardDefinition def,
        ImmutableArray<string> summonedActorInstanceIds)
    {
        var s = state;
        var effects = (card.IsUpgraded && def.UpgradedEffects is not null)
            ? def.UpgradedEffects
            : def.Effects;
        bool hasExhaustSelf = effects.Any(e => e.Action == "exhaustSelf")
            || (def.EffectiveKeywords(card.IsUpgraded)?.Contains("exhaust") ?? false);
        bool hasRetainSelf = effects.Any(e => e.Action == "retainSelf")
            || (def.EffectiveKeywords(card.IsUpgraded)?.Contains("wait") ?? false);
        bool isPower = def.CardType == CardType.Power;
        bool isUnit = def.CardType == CardType.Unit;

        // InstanceId で hand から探す (resume 経路で順序が変わっている可能性に対応)
        int handIdx = -1;
        for (int i = 0; i < s.Hand.Length; i++)
        {
            if (s.Hand[i].InstanceId == card.InstanceId) { handIdx = i; break; }
        }
        if (handIdx < 0)
            throw new InvalidOperationException(
                $"FinalizeCardPlay: card '{card.InstanceId}' not in Hand at finalize time");
        s = s with { Hand = s.Hand.RemoveAt(handIdx) };

        if (hasExhaustSelf)
        {
            s = s with { ExhaustPile = s.ExhaustPile.Add(card) };
        }
        else if (isPower)
        {
            s = s with { PowerCards = s.PowerCards.Add(card) };
        }
        else if (isUnit && TryBindSummonedActors(
                     s, card.InstanceId, summonedActorInstanceIds,
                     out var boundState))
        {
            s = boundState with { SummonHeld = boundState.SummonHeld.Add(card) };
        }
        else if (hasRetainSelf)
        {
            s = s with { Hand = s.Hand.Insert(handIdx, card) };
        }
        else
        {
            s = s with { DiscardPile = s.DiscardPile.Add(card) };
        }

        return s;
    }

    /// <summary>味方専用経路（戦技・選択解決）向けの味方 index 版。</summary>
    private static int FindLivingParticipantCasterIndex(
        BattleState state,
        string actorInstanceId)
    {
        var actor = FindLivingParticipantCaster(
            state, actorInstanceId, allowEnemy: false);
        for (int i = 0; i < state.Allies.Length; i++)
        {
            if (string.Equals(
                    state.Allies[i].InstanceId,
                    actor.InstanceId,
                    StringComparison.Ordinal))
                return i;
        }
        throw new InvalidOperationException(
            $"Participant actor '{actorInstanceId}' is not an allied combat actor.");
    }

    private static CombatActor FindLivingParticipantCaster(
        BattleState state,
        string actorInstanceId,
        bool allowEnemy)
    {
        var actor = FindParticipantCaster(state, actorInstanceId);
        if (actor is null || (actor.Side == ActorSide.Enemy && !allowEnemy))
            throw new InvalidOperationException(
                allowEnemy
                    ? $"Participant actor '{actorInstanceId}' is not a combat actor."
                    : $"Participant actor '{actorInstanceId}' is not an allied combat actor.");
        if (!actor.IsAlive)
            throw new InvalidOperationException(
                $"Participant actor '{actorInstanceId}' is not alive.");
        return actor;
    }

    /// <summary>味方・敵の両方から InstanceId で caster を検索する（敵カード計画にも共通利用）。</summary>
    private static CombatActor? FindParticipantCaster(
        BattleState state,
        string actorInstanceId)
    {
        for (int i = 0; i < state.Allies.Length; i++)
        {
            if (string.Equals(
                    state.Allies[i].InstanceId,
                    actorInstanceId,
                    StringComparison.Ordinal))
                return state.Allies[i];
        }
        for (int i = 0; i < state.Enemies.Length; i++)
        {
            if (string.Equals(
                    state.Enemies[i].InstanceId,
                    actorInstanceId,
                    StringComparison.Ordinal))
                return state.Enemies[i];
        }

        return null;
    }

    private static BattleState ReplaceParticipantCaster(
        BattleState state,
        CombatActor updated)
    {
        for (int i = 0; i < state.Allies.Length; i++)
        {
            if (string.Equals(
                    state.Allies[i].InstanceId,
                    updated.InstanceId,
                    StringComparison.Ordinal))
                return state with { Allies = state.Allies.SetItem(i, updated) };
        }
        for (int i = 0; i < state.Enemies.Length; i++)
        {
            if (string.Equals(
                    state.Enemies[i].InstanceId,
                    updated.InstanceId,
                    StringComparison.Ordinal))
                return state with { Enemies = state.Enemies.SetItem(i, updated) };
        }

        throw new InvalidOperationException(
            $"Card caster '{updated.InstanceId}' is not on the battlefield.");
    }

    private static BattleState IncrementActionRevision(BattleState state)
    {
        if (state.ActionRevision >= long.MaxValue)
            return state;
        long current = Math.Max(0L, state.ActionRevision);
        return state with { ActionRevision = current + 1L };
    }
}
