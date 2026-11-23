using System;
using RoguelikeCardGame.Core.Battle.State;

namespace RoguelikeCardGame.Core.Battle.Engine;

public static partial class BattleEngine
{
    /// <summary>
    /// 対象スロットを切替する。Phase=PlayerInput でのみ呼出可能、
    /// 範囲外 / 死亡スロットで InvalidOperationException。
    /// イベント発火なし（BattleState のみ返す）。
    /// 親 spec §7-3 / Phase 10.2.C spec §4 参照。
    /// </summary>
    public static BattleState SetTarget(BattleState state, ActorSide side, int slotIndex)
    {
        RejectIfParticipantReady(state, participantId: null, "SetTarget");
        var next = SetTargetCore(state, side, slotIndex);
        if (!next.Participants.IsDefaultOrEmpty)
        {
            next = BattleParticipantAccess.CommitRootToParticipant(
                next, next.Participants[0].ParticipantId);
        }
        return IncrementActionRevision(next);
    }

    public static BattleState SetTarget(
        BattleState state,
        string participantId,
        ActorSide side,
        int slotIndex)
    {
        if (!BattleParticipantAccess.TryGet(state, participantId, out _))
            throw new InvalidOperationException(
                $"Battle participant '{participantId}' does not exist.");
        RejectIfParticipantReady(state, participantId, "SetTarget");
        var projected = BattleParticipantAccess.ProjectToRoot(state, participantId);
        var processed = SetTargetCore(projected, side, slotIndex);
        var committed = BattleParticipantAccess.CommitRootToParticipant(
            processed, participantId);
        return IncrementActionRevision(committed);
    }

    private static BattleState SetTargetCore(
        BattleState state,
        ActorSide side,
        int slotIndex)
    {
        if (state.Phase != BattlePhase.PlayerInput)
            throw new InvalidOperationException(
                $"SetTarget requires Phase=PlayerInput, got {state.Phase}");
        RejectWhileCardChoiceIsPending(state, "SetTarget");

        var pool = side == ActorSide.Ally ? state.Allies : state.Enemies;

        if (slotIndex < 0 || slotIndex >= pool.Length)
            throw new InvalidOperationException(
                $"slotIndex {slotIndex} out of range [0, {pool.Length}) for side={side}");

        if (!pool[slotIndex].IsAlive)
            throw new InvalidOperationException(
                $"slot {side}[{slotIndex}] is dead and cannot be targeted");

        return side == ActorSide.Ally
            ? state with { TargetAllyIndex = slotIndex }
            : state with { TargetEnemyIndex = slotIndex };
    }
}
