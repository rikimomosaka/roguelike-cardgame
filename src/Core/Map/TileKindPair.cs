namespace RoguelikeCardGame.Core.Map;

/// <summary>タイル種別のペア。エッジ First → Second の連続を表現する。</summary>
public sealed record TileKindPair(TileKind First, TileKind Second);
