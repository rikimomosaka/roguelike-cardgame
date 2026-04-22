using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Merchant;

public static class MerchantInventoryGenerator
{
    private const int CardCount = 5;
    private const int RelicCount = 2;
    private const int PotionCount = 3;

    public static MerchantInventory Generate(
        DataCatalog catalog, MerchantPrices prices, RunState s, IRng rng)
    {
        var cards = PickCards(catalog, prices, s, rng, CardCount);
        var relics = PickRelics(catalog, prices, s, rng, RelicCount);
        var potions = PickPotions(catalog, prices, rng, PotionCount);
        return new MerchantInventory(
            cards, relics, potions,
            DiscardSlotUsed: false,
            DiscardPrice: prices.DiscardSlotPrice);
    }

    private static ImmutableArray<MerchantOffer> PickCards(
        DataCatalog catalog, MerchantPrices prices, RunState s, IRng rng, int count)
    {
        var candidates = catalog.Cards.Values
            .Where(c => c.Id.StartsWith("reward_") && prices.Cards.ContainsKey(c.Rarity))
            .OrderBy(c => c.Id)
            .ToList();
        return PickFromPool(candidates, count, rng,
            def => new MerchantOffer("card", def.Id, prices.Cards[def.Rarity], false));
    }

    private static ImmutableArray<MerchantOffer> PickRelics(
        DataCatalog catalog, MerchantPrices prices, RunState s, IRng rng, int count)
    {
        var candidates = catalog.Relics.Values
            .Where(r => !s.Relics.Contains(r.Id) && prices.Relics.ContainsKey(r.Rarity))
            .OrderBy(r => r.Id)
            .ToList();
        return PickFromPool(candidates, count, rng,
            def => new MerchantOffer("relic", def.Id, prices.Relics[def.Rarity], false));
    }

    private static ImmutableArray<MerchantOffer> PickPotions(
        DataCatalog catalog, MerchantPrices prices, IRng rng, int count)
    {
        var candidates = catalog.Potions.Values
            .Where(p => prices.Potions.ContainsKey(p.Rarity))
            .OrderBy(p => p.Id)
            .ToList();
        return PickFromPool(candidates, count, rng,
            def => new MerchantOffer("potion", def.Id, prices.Potions[def.Rarity], false));
    }

    private static ImmutableArray<MerchantOffer> PickFromPool<T>(
        List<T> pool, int count, IRng rng,
        System.Func<T, MerchantOffer> makeOffer)
    {
        int take = System.Math.Min(count, pool.Count);
        var remaining = new List<T>(pool);
        var picked = new List<MerchantOffer>(take);
        for (int i = 0; i < take; i++)
        {
            int idx = rng.NextInt(0, remaining.Count);
            picked.Add(makeOffer(remaining[idx]));
            remaining.RemoveAt(idx);
        }
        return picked.ToImmutableArray();
    }
}
