using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Map;

/// <summary>
/// start → boss の 1 ルート上に課される制約。
/// </summary>
/// <param name="PerPathCount">1 ルートでの Kind 別許容個数 [Min, Max]。キー欠落 = 制約なし。</param>
/// <param name="MinEliteRow">Elite を配置できる最小行。これ未満の行では Elite を候補から外す。</param>
/// <param name="ForbiddenConsecutive">First → Second の順にエッジで隣接することを禁止。</param>
public sealed record PathConstraintRule(
    ImmutableDictionary<TileKind, IntRange> PerPathCount,
    int MinEliteRow,
    ImmutableArray<TileKindPair> ForbiddenConsecutive);
