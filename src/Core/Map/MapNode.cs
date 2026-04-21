using System;
using System.Collections.Immutable;
using System.Linq;

namespace RoguelikeCardGame.Core.Map;

/// <summary>
/// ダンジョンマップの 1 マス。
/// </summary>
/// <remarks>
/// VR (Udon#) 移植時：record → sealed class、ImmutableArray&lt;int&gt; → int[] に置換。
/// ImmutableArray&lt;T&gt; は struct のため record の自動生成 Equals では内容比較されない。
/// そのため Equals / GetHashCode を明示的にオーバーライドする。
/// </remarks>
public sealed record MapNode(
    int Id,
    int Row,
    int Column,
    TileKind Kind,
    ImmutableArray<int> OutgoingNodeIds)
{
    public bool Equals(MapNode? other) =>
        other is not null &&
        Id == other.Id &&
        Row == other.Row &&
        Column == other.Column &&
        Kind == other.Kind &&
        OutgoingNodeIds.SequenceEqual(other.OutgoingNodeIds);

    public override int GetHashCode()
    {
        var hash = HashCode.Combine(Id, Row, Column, Kind);
        foreach (var id in OutgoingNodeIds)
            hash = HashCode.Combine(hash, id);
        return hash;
    }
}
