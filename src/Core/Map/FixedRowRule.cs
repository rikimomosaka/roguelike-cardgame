namespace RoguelikeCardGame.Core.Map;

/// <summary>指定行の全ノードを単一の <see cref="TileKind"/> に固定するルール。</summary>
public sealed record FixedRowRule(int Row, TileKind Kind);
