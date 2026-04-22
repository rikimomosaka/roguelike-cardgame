using System.Collections.Immutable;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Merchant;

public sealed record MerchantPrices(
    ImmutableDictionary<CardRarity, int> Cards,
    ImmutableDictionary<CardRarity, int> Relics,
    ImmutableDictionary<CardRarity, int> Potions,
    int DiscardSlotPrice);
