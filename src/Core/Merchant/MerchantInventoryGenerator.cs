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
    private const int ClassCardCount = 3;
    private const int UniversalCardCount = 2;
    private const int RelicCount = 2;
    private const int PotionCount = 3;

    /// <summary>1 run 内で除去マスを使うたびに、次回価格へ加算される量。</summary>
    public const int DiscardPriceIncrement = 25;

    public static MerchantInventory Generate(
        DataCatalog catalog, MerchantPrices prices, RunState s, IRng rng)
    {
        var cards = PickCards(catalog, prices, s, rng);
        var relics = PickRelics(catalog, prices, s, rng, RelicCount);
        var potions = PickPotions(catalog, prices, rng, PotionCount);
        int rawDiscard = prices.DiscardSlotPrice + DiscardPriceIncrement * s.DiscardUsesSoFar;

        // Phase 10.6.B T4: shopPriceMultiplier を全 offer と DiscardPrice に適用 (relic 経由の値引き / 値上げ)
        cards = cards.Select(o => o with {
            Price = Relics.PassiveModifiers.ApplyShopPriceMultiplier(o.Price, s, catalog)
        }).ToImmutableArray();
        relics = relics.Select(o => o with {
            Price = Relics.PassiveModifiers.ApplyShopPriceMultiplier(o.Price, s, catalog)
        }).ToImmutableArray();
        potions = potions.Select(o => o with {
            Price = Relics.PassiveModifiers.ApplyShopPriceMultiplier(o.Price, s, catalog)
        }).ToImmutableArray();
        int discardPrice = Relics.PassiveModifiers.ApplyShopPriceMultiplier(rawDiscard, s, catalog);

        return new MerchantInventory(
            cards, relics, potions,
            DiscardSlotUsed: false,
            DiscardPrice: discardPrice);
    }

    private static ImmutableArray<MerchantOffer> PickCards(
        DataCatalog catalog, MerchantPrices prices, RunState s, IRng rng)
    {
        if (!catalog.Characters.TryGetValue(s.CharacterId, out var character))
            throw new System.InvalidOperationException(
                $"Character \"{s.CharacterId}\" is not configured in the data catalog");

        // Why: prices.Cards に Token rarity の価格は存在しないので
        // ContainsKey で除外されるが、将来 prices に Token 価格が誤って
        // 追加された場合でも token カードが商人在庫に並ばないよう明示的に
        // 除外する防御フィルタ (Phase 10.5.G)。
        var pricedRewards = catalog.Cards.Values
            .Where(c => c.Rarity != CardRarity.Token && !c.IsEnemyCard)
            .Where(c => prices.Cards.ContainsKey(c.Rarity))
            .OrderBy(c => c.Id)
            .ToList();

        // Class and Universal inventory slots are independent. If either pool is
        // short, the other pool must not fill its reserved slots.
        var classOffers = PickFromPool(
            pricedRewards.Where(card => CardPoolRules.IsClassRewardFor(card, character)).ToList(),
            ClassCardCount,
            rng,
            def => new MerchantOffer("card", def.Id, prices.Cards[def.Rarity], false));
        var universalOffers = PickFromPool(
            pricedRewards.Where(card => CardPoolRules.IsUniversalRewardFor(card, character)).ToList(),
            UniversalCardCount,
            rng,
            def => new MerchantOffer("card", def.Id, prices.Cards[def.Rarity], false));
        return classOffers.AddRange(universalOffers);
    }

    private static ImmutableArray<MerchantOffer> PickRelics(
        DataCatalog catalog, MerchantPrices prices, RunState s, IRng rng, int count)
    {
        var candidates = Relics.RelicPoolHelper.GetNormalAcquisitionRelics(catalog)
            .Where(r => !s.Relics.Contains(r.Id) && prices.Relics.ContainsKey(r.Rarity))
            .OrderBy(r => r.Id, System.StringComparer.Ordinal)
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
