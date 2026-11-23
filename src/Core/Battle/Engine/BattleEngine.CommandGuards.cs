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

    /// <summary>
    /// Ready is a reversible planning lock. A participant must explicitly
    /// unready before issuing another planning command; enforcing this in Core
    /// also protects stale clients and non-HTTP callers.
    /// </summary>
    private static void RejectIfParticipantReady(
        BattleState state,
        string? participantId,
        string commandName)
    {
        if (state.Participants.IsDefaultOrEmpty)
            return;

        BattleParticipantState participant;
        if (string.IsNullOrWhiteSpace(participantId))
        {
            participant = BattleParticipantAccess.GetPrimary(state);
        }
        else if (!BattleParticipantAccess.TryGet(
                     state, participantId, out participant!))
        {
            throw new InvalidOperationException(
                $"Battle participant '{participantId}' does not exist.");
        }

        if (participant.IsReady)
            throw new InvalidOperationException(
                $"{commandName} cannot run while participant " +
                $"'{participant.ParticipantId}' is Ready; unready first");
    }
}
