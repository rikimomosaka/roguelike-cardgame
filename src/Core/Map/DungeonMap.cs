using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace RoguelikeCardGame.Core.Map;

/// <summary>
/// 生成済みのダンジョンマップ。ノード集合と Start/Boss の Id を保持する。
/// </summary>
/// <remarks>
/// ImmutableArray&lt;T&gt; は struct のため record の自動生成 Equals では内容比較されない（参照比較になる）。
/// <see cref="MapNode"/> と同じ理由で Equals / GetHashCode を明示的にオーバーライドする。
/// </remarks>
public sealed record DungeonMap(
    ImmutableArray<MapNode> Nodes,
    int StartNodeId,
    int BossNodeId)
{
    /// <summary>Id でノードを取得。Id は <see cref="Nodes"/> の index と一致する想定。</summary>
    public MapNode GetNode(int id) => Nodes[id];

    /// <summary>指定行のノードを列挙する（単純走査）。</summary>
    public IEnumerable<MapNode> NodesInRow(int row) => Nodes.Where(n => n.Row == row);

    public bool Equals(DungeonMap? other) =>
        other is not null &&
        StartNodeId == other.StartNodeId &&
        BossNodeId == other.BossNodeId &&
        Nodes.SequenceEqual(other.Nodes);

    public override int GetHashCode()
    {
        var hash = HashCode.Combine(StartNodeId, BossNodeId);
        foreach (var node in Nodes)
            hash = HashCode.Combine(hash, node);
        return hash;
    }
}
