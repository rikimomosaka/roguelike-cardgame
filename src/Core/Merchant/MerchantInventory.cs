using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Merchant;

public sealed record MerchantInventory(
    ImmutableArray<MerchantOffer> Cards,
    ImmutableArray<MerchantOffer> Relics,
    ImmutableArray<MerchantOffer> Potions,
    bool DiscardSlotUsed,
    int DiscardPrice,
    bool LeftSoFar = false);
