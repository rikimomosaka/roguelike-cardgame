using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.State;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// 死亡判定後に対象を自動切替するヘルパー。最小スロット生存者へ。
/// 親 spec §7-4 参照。
/// </summary>
internal static class TargetingAutoSwitch
{
    public static BattleState Apply(BattleState state)
    {
        if (state.Participants.IsDefaultOrEmpty)
        {
            return state with
            {
                TargetEnemyIndex = NormalizeTargetIndex(
                    state.Enemies, state.TargetEnemyIndex),
                TargetAllyIndex = NormalizeTargetIndex(
                    state.Allies, state.TargetAllyIndex),
            };
        }

        var participants = ImmutableArray.CreateBuilder<BattleParticipantState>(
            state.Participants.Length);
        foreach (var participant in state.Participants)
        {
            participants.Add(participant with
            {
                TargetEnemyIndex = NormalizeTargetIndex(
                    state.Enemies, participant.TargetEnemyIndex),
                TargetAllyIndex = NormalizeTargetIndex(
                    state.Allies, participant.TargetAllyIndex),
            });
        }

        var updatedParticipants = participants.MoveToImmutable();
        var primary = updatedParticipants[0];
        return state with
        {
            Participants = updatedParticipants,
            // Participant zero is authoritative; the legacy root remains its
            // compatibility mirror even when secondary targets differ.
            TargetEnemyIndex = primary.TargetEnemyIndex,
            TargetAllyIndex = primary.TargetAllyIndex,
        };
    }

    private static int? NormalizeTargetIndex(
        ImmutableArray<CombatActor> actors,
        int? targetIndex)
    {
        if (targetIndex is null)
            return null;

        int index = targetIndex.Value;
        if (index >= 0 && index < actors.Length && actors[index].IsAlive)
            return index;

        // Target indices are DTO array indices. SlotIndex is stable visual
        // placement metadata and must never be written into this field.
        for (int candidateIndex = 0; candidateIndex < actors.Length; candidateIndex++)
        {
            if (actors[candidateIndex].IsAlive)
                return candidateIndex;
        }

        return null;
    }
}
