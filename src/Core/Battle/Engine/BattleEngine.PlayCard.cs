using System;
using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;

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
        bool isWild = keywords?.Contains("wild") == true;
        bool isSuperWild = keywords?.Contains("superwild") == true;

        bool isContinuing =
            state.NextCardComboFreePass ? true
          : matchesNormal              ? true
          : (isWild || isSuperWild)    ? true
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
        bool newFreePass = isSuperWild;

        var s = state with
        {
            Energy = state.Energy - payCost,
            ComboCount = newCombo,
            LastPlayedOrigCost = newLastCost,
            NextCardComboFreePass = newFreePass,
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

        // 10.2.D: summon 成功フラグを追跡
        bool summonSucceeded = false;

        foreach (var eff in effects)
        {
            // 10.2.C: per-effect comboMin filter（PlayCard 経路のみ）
            if (eff.ComboMin is { } min && newCombo < min)
                continue;

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

        // 10.2.E 追加: OnCardPlay レリック発動（effect 適用後・カード移動前）
        var (afterRelic, evsRelic) = RelicTriggerProcessor.Fire(
            s, RelicTrigger.OnCardPlay, catalog, rng, orderStart: order);
        s = afterRelic;
        foreach (var ev in evsRelic) { events.Add(ev with { Order = order++ }); }

        // 10.2.D: 5 段優先順位（exhaustSelf → Power → Unit+success → retainSelf → Discard）
        bool hasExhaustSelf = effects.Any(e => e.Action == "exhaustSelf");
        bool hasRetainSelf = effects.Any(e => e.Action == "retainSelf");
        bool isPower = def.CardType == CardType.Power;
        bool isUnit = def.CardType == CardType.Unit;

        s = s with { Hand = s.Hand.RemoveAt(handIndex) };

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
            // hand の元の位置に戻す
            s = s with { Hand = s.Hand.Insert(handIndex, card) };
        }
        else
        {
            s = s with { DiscardPile = s.DiscardPile.Add(card) };
        }

        return (s, events);
    }
}
