using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Data;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// ターン終了処理。Phase 10.2.C でコンボ 3 フィールドのリセットを追加。
/// Phase 10.2.D で retainSelf-aware 手札整理 + DataCatalog 引数追加。
/// 10.2.E で OnTurnEnd レリックが追加される予定。
/// 親 spec §4-6 参照。
/// </summary>
internal static class TurnEndProcessor
{
    public static (BattleState, IReadOnlyList<BattleEvent>) Process(
        BattleState state, DataCatalog catalog)
    {
        var allies = state.Allies.Select(ResetActor).ToImmutableArray();
        var enemies = state.Enemies.Select(ResetActor).ToImmutableArray();

        // 10.2.D: retainSelf-aware 手札整理
        var keepInHand = ImmutableArray.CreateBuilder<BattleCardInstance>();
        var newDiscard = state.DiscardPile.ToBuilder();
        foreach (var card in state.Hand)
        {
            if (!catalog.TryGetCard(card.CardDefinitionId, out var def))
            {
                newDiscard.Add(card);
                continue;
            }
            var effects = (card.IsUpgraded && def.UpgradedEffects is not null)
                ? def.UpgradedEffects : def.Effects;
            if (effects.Any(e => e.Action == "retainSelf"))
                keepInHand.Add(card);
            else
                newDiscard.Add(card);
        }

        var next = state with
        {
            Allies = allies,
            Enemies = enemies,
            Hand = keepInHand.ToImmutable(),
            DiscardPile = newDiscard.ToImmutable(),
            ComboCount = 0,                       // 10.2.C
            LastPlayedOrigCost = null,            // 10.2.C
            NextCardComboFreePass = false,        // 10.2.C
        };
        return (next, Array.Empty<BattleEvent>());
    }

    private static CombatActor ResetActor(CombatActor a) => a with
    {
        Block = BlockPool.Empty,
        AttackSingle = AttackPool.Empty,
        AttackRandom = AttackPool.Empty,
        AttackAll = AttackPool.Empty,
    };
}
