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
        bool isWild = def.Keywords?.Contains("wild") == true;
        bool isSuperWild = def.Keywords?.Contains("superwild") == true;

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

        foreach (var eff in effects)
        {
            // 10.2.C: per-effect comboMin filter（PlayCard 経路のみ）
            if (eff.ComboMin is { } min && newCombo < min)
                continue;

            var (afterEffect, evs) = EffectApplier.Apply(s, caster, eff, rng, catalog);
            s = afterEffect;
            foreach (var ev in evs)
            {
                events.Add(ev with { Order = order });
                order++;
            }
            caster = s.Allies[0];
        }

        var newHand = s.Hand.RemoveAt(handIndex);
        var newDiscard = s.DiscardPile.Add(card);
        s = s with { Hand = newHand, DiscardPile = newDiscard };

        return (s, events);
    }
}
