using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Map;

namespace RoguelikeCardGame.Core.Run;

/// <summary>
/// RunState を純関数で遷移させるアクション群。UI・通信非依存。
/// </summary>
public static class RunActions
{
    /// <summary>
    /// 現在地から target ノードへの移動を反映した新しい RunState を返す。
    /// target は現在ノードの OutgoingNodeIds に含まれる必要がある。違反時 <see cref="ArgumentException"/>。
    /// </summary>
    public static RunState SelectNextNode(RunState state, DungeonMap map, int targetNodeId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(map);

        EnsureCurrentNodeIsResolved(state, map);

        if (targetNodeId < 0 || targetNodeId >= map.Nodes.Length)
            throw new ArgumentException(
                $"targetNodeId {targetNodeId} is out of range [0..{map.Nodes.Length - 1}]",
                nameof(targetNodeId));

        var current = map.GetNode(state.CurrentNodeId);
        if (!current.OutgoingNodeIds.Contains(targetNodeId))
            throw new ArgumentException(
                $"targetNodeId {targetNodeId} is not adjacent to current node {state.CurrentNodeId}",
                nameof(targetNodeId));

        return state with
        {
            CurrentNodeId = targetNodeId,
            VisitedNodeIds = state.VisitedNodeIds.Add(targetNodeId),
        };
    }

    private static void EnsureCurrentNodeIsResolved(RunState state, DungeonMap map)
    {
        if (state.Progress != RunProgress.InProgress)
            throw new InvalidOperationException("Cannot move after the run has ended");
        if (state.ActiveBattle is not null)
            throw new InvalidOperationException("Cannot leave the current node while a battle is active");
        if (state.ActiveReward is not null)
            throw new InvalidOperationException("Cannot leave the current node while a reward is pending");
        if (state.ActiveEvent is { ChosenIndex: null })
            throw new InvalidOperationException("Cannot leave the current node before choosing an event option");
        if (state.ActiveRestPending && !state.ActiveRestCompleted)
            throw new InvalidOperationException("Cannot leave the current node before choosing a rest action");
        if (state.ActiveActStartRelicChoice is not null)
            throw new InvalidOperationException("Cannot leave the current node before choosing an act-start relic");
        if (state.CurrentNodeId == map.StartNodeId
            && !state.VisitedNodeIds.Contains(state.CurrentNodeId))
            throw new InvalidOperationException(
                "Cannot leave the start node before completing the act-start relic choice");

        // Merchant inventory is intentionally not checked. Leaving a shop without
        // purchasing anything is a valid player choice.
    }
}
