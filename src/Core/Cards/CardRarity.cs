namespace RoguelikeCardGame.Core.Cards;

/// <summary>カードのレアリティ。JSON では整数として保存する。</summary>
public enum CardRarity
{
    Promo = 0,
    Common = 1,
    Rare = 2,
    Epic = 3,
    Legendary = 4,
    /// <summary>
    /// バトル中の addCard effect で手札に加えられる token カード。
    /// 報酬・商人プールには出現しない (RewardGenerator / MerchantInventoryGenerator で除外)。
    /// 図鑑の通常コレクション対象外。
    /// </summary>
    Token = 5,
}
