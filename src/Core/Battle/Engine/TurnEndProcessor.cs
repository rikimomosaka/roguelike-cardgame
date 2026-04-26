using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// ターン終了処理。Phase 10.2.C でコンボ 3 フィールドのリセットを追加。
/// 10.2.E で OnTurnEnd レリック / 10.2.D で retainSelf 対応の手札整理が追加される。
/// 親 spec §4-6 参照。
/// </summary>
internal static class TurnEndProcessor
{
    public static (BattleState, IReadOnlyList<BattleEvent>) Process(BattleState state)
    {
        var allies = state.Allies.Select(ResetActor).ToImmutableArray();
        var enemies = state.Enemies.Select(ResetActor).ToImmutableArray();
        var newDiscard = state.DiscardPile.AddRange(state.Hand);
        var next = state with
        {
            Allies = allies,
            Enemies = enemies,
            Hand = ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile = newDiscard,
            ComboCount = 0,                       // 10.2.C
            LastPlayedOrigCost = null,            // 10.2.C
            NextCardComboFreePass = false,        // 10.2.C
        };
        return (next, System.Array.Empty<BattleEvent>());
    }

    private static CombatActor ResetActor(CombatActor a) => a with
    {
        Block = BlockPool.Empty,
        AttackSingle = AttackPool.Empty,
        AttackRandom = AttackPool.Empty,
        AttackAll = AttackPool.Empty,
    };
}
