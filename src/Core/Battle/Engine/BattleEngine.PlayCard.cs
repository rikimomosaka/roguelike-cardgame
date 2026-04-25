using System;
using System.Collections.Generic;
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

        // 10.2.A: コンボ軽減なし。CostOverride 優先 → 強化版 cost → 通常 cost
        int? cost = card.CostOverride ?? (card.IsUpgraded ? def.UpgradedCost ?? def.Cost : def.Cost);
        if (cost is null)
            throw new InvalidOperationException($"card '{def.Id}' is unplayable (cost=null)");
        if (state.Energy < cost.Value)
            throw new InvalidOperationException($"insufficient energy: have {state.Energy}, need {cost}");

        // 対象切替（10.2.A は基本機能のみ）
        var s = state with
        {
            Energy = state.Energy - cost.Value,
            TargetEnemyIndex = targetEnemyIndex ?? state.TargetEnemyIndex,
            TargetAllyIndex = targetAllyIndex ?? state.TargetAllyIndex,
        };

        var events = new List<BattleEvent>
        {
            new(BattleEventKind.PlayCard, Order: 0,
                CasterInstanceId: state.Allies[0].InstanceId,
                CardId: def.Id,
                Amount: cost.Value),
        };

        var caster = s.Allies[0]; // 10.2.A: caster = hero 固定
        int order = 1;

        // 強化状態に応じたエフェクトリストを選択
        var effects = (card.IsUpgraded && def.UpgradedEffects is not null)
            ? def.UpgradedEffects
            : def.Effects;

        foreach (var eff in effects)
        {
            var (afterEffect, evs) = EffectApplier.Apply(s, caster, eff, rng);
            s = afterEffect;
            // events に order を振り直す
            foreach (var ev in evs)
            {
                events.Add(ev with { Order = order });
                order++;
            }
            // caster は Pool 加算で更新されるので再取得
            caster = s.Allies[0];
        }

        // カードを Hand → DiscardPile へ移動（10.2.A: exhaust/retain/Power/Unit 未対応）
        var newHand = s.Hand.RemoveAt(handIndex);
        var newDiscard = s.DiscardPile.Add(card);
        s = s with { Hand = newHand, DiscardPile = newDiscard };

        return (s, events);
    }
}
