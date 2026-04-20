namespace RoguelikeCardGame.Core.Cards;

/// <summary>カードのレアリティ。JSON では整数として保存する。</summary>
public enum CardRarity
{
    Promo = 0,
    Common = 1,
    Rare = 2,
    Epic = 3,
    Legendary = 4,
}
