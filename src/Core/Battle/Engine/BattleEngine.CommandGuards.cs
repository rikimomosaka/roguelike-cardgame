using System;
using RoguelikeCardGame.Core.Battle.State;

namespace RoguelikeCardGame.Core.Battle.Engine;

public static partial class BattleEngine
{
    /// <summary>
    /// カード効果の選択待ちは、プレイ中カードの解決途中である。
    /// ResolveCardChoice 以外のcommandでstateを進めると、そのカードを見失うため拒否する。
    /// </summary>
    private static void RejectWhileCardChoiceIsPending(
        BattleState state, string commandName)
    {
        if (state.PendingCardPlay is not null)
            throw new InvalidOperationException(
                $"{commandName} cannot run while PendingCardPlay is set; " +
                "resolve via ResolveCardChoice first");
    }
}
