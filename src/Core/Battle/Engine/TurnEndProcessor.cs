using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// ターン終了処理。10.2.A は最小限（Block / AttackPool リセット、手札全捨て）。
/// 10.2.B で OnTurnEnd レリック / コンボリセット, 10.2.D で retainSelf 対応が追加される。
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
