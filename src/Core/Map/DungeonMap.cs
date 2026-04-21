using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace RoguelikeCardGame.Core.Map;

/// <summary>
/// 生成済みのダンジョンマップ。ノード集合と Start/Boss の Id を保持する。
/// </summary>
public sealed record DungeonMap(
    ImmutableArray<MapNode> Nodes,
    int StartNodeId,
    int BossNodeId)
{
    /// <summary>Id でノードを取得。Id は <see cref="Nodes"/> の index と一致する想定。</summary>
    public MapNode GetNode(int id) => Nodes[id];

    /// <summary>指定行のノードを列挙する（単純走査）。</summary>
    public IEnumerable<MapNode> NodesInRow(int row) => Nodes.Where(n => n.Row == row);
}
