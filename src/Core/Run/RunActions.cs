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
}
