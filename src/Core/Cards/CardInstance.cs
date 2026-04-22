namespace RoguelikeCardGame.Core.Cards;

/// <summary>
/// RunState.Deck の要素。カード ID と強化状態を持つ。
/// マスター定義は DataCatalog.Cards[Id] で引く。
/// </summary>
public sealed record CardInstance(string Id, bool Upgraded = false);
