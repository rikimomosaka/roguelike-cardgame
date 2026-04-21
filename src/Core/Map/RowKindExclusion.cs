namespace RoguelikeCardGame.Core.Map;

/// <summary>指定行で <see cref="ExcludedKind"/> を割り当てないルール。</summary>
public sealed record RowKindExclusion(int Row, TileKind ExcludedKind);
