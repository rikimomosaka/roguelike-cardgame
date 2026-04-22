namespace RoguelikeCardGame.Core.Merchant;

/// <summary>商人在庫の 1 品目。`Kind` は "card" / "relic" / "potion"。</summary>
public sealed record MerchantOffer(
    string Kind,
    string Id,
    int Price,
    bool Sold);
