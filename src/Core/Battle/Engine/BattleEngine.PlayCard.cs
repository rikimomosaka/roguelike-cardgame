using System;
using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
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
        if (state.Phase != BattlePhase.PlayerInput)
            throw new InvalidOperationException($"PlayCard requires Phase=PlayerInput, got {state.Phase}");
        if (handIndex < 0 || handIndex >= state.Hand.Length)
            throw new InvalidOperationException($"handIndex {handIndex} out of range [0, {state.Hand.Length})");

        // Phase 10.5.M2-Choose: 既存 pending を resolve しないと PlayCard 不可
        if (state.PendingCardPlay is not null)
            throw new InvalidOperationException(
                "Cannot play card while PendingCardPlay is set; resolve via ResolveCardChoice first");

        var card = state.Hand[handIndex];
        if (!catalog.TryGetCard(card.CardDefinitionId, out var def))
            throw new InvalidOperationException($"card '{card.CardDefinitionId}' not in catalog");

        // 10.2.C: 元コスト算定（CostOverride 無視）
        int? origCost = card.IsUpgraded ? def.UpgradedCost ?? def.Cost : def.Cost;
        if (origCost is null)
            throw new InvalidOperationException($"card '{def.Id}' is unplayable (cost=null)");
        int actualCost = origCost.Value;

        // 10.2.C: コンボ判定
        bool matchesNormal =
            state.LastPlayedOrigCost is { } prev && actualCost == prev + 1;
        // Why: 強化により獲得するキーワード (例: starter_summon_3+ で superwild) を反映するため、
        // 単一 Keywords ではなく EffectiveKeywords(IsUpgraded) を参照する。
        var keywords = def.EffectiveKeywords(card.IsUpgraded);
        bool hasWild = keywords?.Contains("wild") == true;
        bool hasSuperWild = keywords?.Contains("superwild") == true;
        // 10.5.M4: ワイルド/スーパーワイルドは 1 コンボ連鎖につき最初の 1 回しか発動しない。
        // すでに WildUsedInCurrentCombo が立っていればキーワード効果を無効化する。
        bool wildEffective = (hasWild || hasSuperWild) && !state.WildUsedInCurrentCombo;

        bool isContinuing =
            state.NextCardComboFreePass ? true
          : matchesNormal              ? true
          : wildEffective              ? true
          : false;

        bool isReduced = matchesNormal;

        // 10.2.C: payCost 算定
        int basePay = card.CostOverride ?? actualCost;
        int payCost = Math.Max(0, basePay - (isReduced ? 1 : 0));

        if (state.Energy < payCost)
            throw new InvalidOperationException($"insufficient energy: have {state.Energy}, need {payCost}");

        // 10.2.C: combo フィールド更新
        int newCombo = isContinuing ? state.ComboCount + 1 : 1;
        int? newLastCost = actualCost;
        // M4: スーパーワイルドの「次カード自動継続」効果も「初回のみ」。
        bool newFreePass = wildEffective && hasSuperWild;
        // M4: ワイルド系効果が今回発動した、または既に発動済 (連鎖継続中) なら立て続ける。
        //   コンボ切断 (newCombo == 1) でリセット。
        bool newWildUsed = newCombo == 1
            ? false
            : state.WildUsedInCurrentCombo || wildEffective;

        var s = state with
        {
            Energy = state.Energy - payCost,
            ComboCount = newCombo,
            LastPlayedOrigCost = newLastCost,
            NextCardComboFreePass = newFreePass,
            WildUsedInCurrentCombo = newWildUsed,
            TargetEnemyIndex = targetEnemyIndex ?? state.TargetEnemyIndex,
            TargetAllyIndex = targetAllyIndex ?? state.TargetAllyIndex,
        };

        var events = new List<BattleEvent>
        {
            new(BattleEventKind.PlayCard, Order: 0,
                CasterInstanceId: state.Allies[0].InstanceId,
                CardId: def.Id,
                Amount: payCost),
        };

        var caster = s.Allies[0];
        int order = 1;

        var effects = (card.IsUpgraded && def.UpgradedEffects is not null)
            ? def.UpgradedEffects
            : def.Effects;

        var (afterEffects, effectEvents, summonSucceeded, _) = ApplyEffectsFrom(
            s, caster, card, effects, startIndex: 0, newCombo, summonSucceededIn: false,
            ref order, rng, catalog);
        s = afterEffects;
        events.AddRange(effectEvents);

        // Phase 10.5.M2-Choose: pause 中は OnPlayCard / カード移動 / Power トリガを発火せず即返却
        if (s.PendingCardPlay is not null)
            return (s, events);

        // Phase 10.5.M2-Choose T4: 終盤共通処理 (relic / カード移動 / power triggers) を切り出し。
        // 同じ helper を ResolveCardChoice からも呼ぶ。move 対象 card は handIndex ではなく
        // InstanceId で検索 (resume で hand 順序が変わっている可能性に対応)。
        return FinalizeCardPlay(s, card, def, summonSucceeded, events, ref order, rng, catalog);
    }

    /// <summary>
    /// Phase 10.5.M2-Choose: PlayCard / ResolveCardChoice 両方から呼ばれる、
    /// effect 適用完了後の共通処理。OnPlayCard relic 発火 → 5 段カード移動 →
    /// PowerTriggerProcessor (OnPlayCard / OnCombo) を順次実行する。
    /// 移動対象 card は handIndex ではなく InstanceId で検索する (resume 経路で
    /// hand 順序が変わっている可能性があるため)。
    /// </summary>
    private static (BattleState, IReadOnlyList<BattleEvent>) FinalizeCardPlay(
        BattleState state, BattleCardInstance card, CardDefinition def,
        bool summonSucceeded, List<BattleEvent> events, ref int order,
        IRng rng, DataCatalog catalog)
    {
        var s = state;

        // 10.2.E 追加: OnPlayCard レリック発動（effect 適用後・カード移動前）
        // Phase 10.5.L1.5: trigger ID を power 側と統一 ("OnCardPlay" → "OnPlayCard")。
        var (afterRelic, evsRelic) = RelicTriggerProcessor.Fire(
            s, "OnPlayCard", catalog, rng, orderStart: order);
        s = afterRelic;
        foreach (var ev in evsRelic) { events.Add(ev with { Order = order++ }); }

        // 10.2.D: 5 段優先順位（exhaustSelf → Power → Unit+success → retainSelf → Discard）
        // 10.5.M2/M3: retainSelf / exhaustSelf は keyword "wait" / "exhaust" で代替可能化。
        // 後方互換のため action と keyword の両方をチェック。
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
        else if (isUnit && summonSucceeded)
        {
            s = s with { SummonHeld = s.SummonHeld.Add(card) };
            // 直前に追加された召喚 actor の AssociatedSummonHeldInstanceId に card.InstanceId を設定
            int lastIdx = s.Allies.Length - 1;
            if (lastIdx >= 0
                && s.Allies[lastIdx].DefinitionId != "hero"
                && s.Allies[lastIdx].AssociatedSummonHeldInstanceId is null)
            {
                var summonActor = s.Allies[lastIdx];
                s = s with { Allies = s.Allies.SetItem(
                    lastIdx, summonActor with { AssociatedSummonHeldInstanceId = card.InstanceId }) };
            }
        }
        else if (hasRetainSelf)
        {
            // hand の元の位置に戻す。handIdx は上の throw で >= 0 が保証されている。
            s = s with { Hand = s.Hand.Insert(handIdx, card) };
        }
        else
        {
            s = s with { DiscardPile = s.DiscardPile.Add(card) };
        }

        // 10.5.E: destination pile 振り分け後の power トリガ発動
        // OnPlayCard はカード移動完了後 (Power カード自身が PowerCards に居る状態で) 発火
        var (afterOnPlay, evsOnPlay) = PowerTriggerProcessor.Fire(
            s, "OnPlayCard", catalog, rng, orderStart: order);
        s = afterOnPlay;
        foreach (var ev in evsOnPlay) { events.Add(ev with { Order = order++ }); }

        // OnCombo は combo update 後で fire (combo は冒頭で update 済み)
        var (afterCombo, evsCombo) = PowerTriggerProcessor.FireOnCombo(
            s, s.ComboCount, catalog, rng, orderStart: order);
        s = afterCombo;
        foreach (var ev in evsCombo) { events.Add(ev with { Order = order++ }); }

        return (s, events);
    }

    /// <summary>
    /// Phase 10.5.M2-Choose: effect ループを resume 可能な形に切り出した内部 helper。
    /// startIndex から effects[i] を順次適用、summonSucceeded は in-out で受け渡す。
    /// PlayCard 初回呼出時は startIndex=0 / summonSucceededIn=false から、
    /// 後の T3-T4 で ResolveCardChoice からの resume では pending.EffectIndex+1 / pending.SummonSucceededBefore から呼ぶ予定。
    /// T2 時点では behavior 変更なし、PlayCard からのみ呼ばれる。
    /// </summary>
    /// <param name="card">T2 では未使用。T3 で choose effect 検出時に PendingCardPlay.CardInstanceId として参照される。</param>
    /// <returns>(更新後 state, 発火 events, 最終 summonSucceeded フラグ, 次に処理する effect index = effects.Count なら全完了)</returns>
    private static (BattleState state, List<BattleEvent> events, bool summonSucceeded, int nextIndex) ApplyEffectsFrom(
        BattleState state, CombatActor caster, BattleCardInstance card,
        IReadOnlyList<CardEffect> effects, int startIndex, int newCombo,
        bool summonSucceededIn, ref int order,
        IRng rng, DataCatalog catalog)
    {
        var s = state;
        var events = new List<BattleEvent>();
        bool summonSucceeded = summonSucceededIn;
        for (int i = startIndex; i < effects.Count; i++)
        {
            var eff = effects[i];
            // 10.5.E: Trigger 指定 effect は即時実行ではなく、PowerTriggerProcessor 経由で対応
            if (!string.IsNullOrEmpty(eff.Trigger)) continue;

            // 10.2.C: per-effect comboMin filter（PlayCard 経路のみ）
            if (eff.ComboMin is { } min && newCombo < min) continue;

            // Phase 10.5.M2-Choose: choose effect で pause が必要なら early return
            // I-1 fix: プレイ中のカード自身を candidate から除外するため card.InstanceId を渡す。
            if (EffectApplier.NeedsPlayerChoice(s, eff, catalog, card.InstanceId))
            {
                var pending = new PendingCardPlay(
                    CardInstanceId: card.InstanceId,
                    EffectIndex: i,
                    SummonSucceededBefore: summonSucceeded,
                    Choice: EffectApplier.BuildPendingChoice(s, eff, catalog, card.InstanceId));
                var paused = s with { PendingCardPlay = pending };
                return (paused, events, summonSucceeded, i);  // i を nextIndex として返す (== pause 位置)
            }

            int beforeAlliesLength = s.Allies.Length;
            var (afterEffect, evs) = EffectApplier.Apply(s, caster, eff, rng, catalog);
            s = afterEffect;
            foreach (var ev in evs)
            {
                events.Add(ev with { Order = order });
                order++;
            }
            caster = s.Allies[0];

            // 10.2.D: summon 成功検出
            if (eff.Action == "summon" && s.Allies.Length > beforeAlliesLength)
                summonSucceeded = true;
        }
        return (s, events, summonSucceeded, effects.Count);
    }
}
