using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Merchant;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Merchant;

public class MerchantInventoryGeneratorTests
{
    private static readonly DataCatalog Catalog = EmbeddedDataLoader.LoadCatalog();

    private static RunState Base() =>
        RunState.NewSoloRun(
            Catalog, 1UL, 0,
            ImmutableDictionary<int, TileKind>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            new DateTimeOffset(2026, 4, 22, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Generate_Yields5Cards2Relics3Potions_AllUnsold()
    {
        var inv = MerchantInventoryGenerator.Generate(
            Catalog, Catalog.MerchantPrices!, Base(), new SequentialRng(1UL));
        Assert.Equal(5, inv.Cards.Length);
        Assert.Equal(2, inv.Relics.Length);
        Assert.Equal(3, inv.Potions.Length);
        Assert.All(inv.Cards.Concat(inv.Relics).Concat(inv.Potions), o => Assert.False(o.Sold));
        Assert.False(inv.DiscardSlotUsed);
        Assert.Equal(75, inv.DiscardPrice);
    }

    [Fact]
    public void Generate_DiscardPriceScalesWithDiscardUsesSoFar()
    {
        var s0 = Base() with { DiscardUsesSoFar = 0 };
        var s1 = Base() with { DiscardUsesSoFar = 1 };
        var s3 = Base() with { DiscardUsesSoFar = 3 };
        Assert.Equal(75, MerchantInventoryGenerator.Generate(
            Catalog, Catalog.MerchantPrices!, s0, new SequentialRng(1UL)).DiscardPrice);
        Assert.Equal(100, MerchantInventoryGenerator.Generate(
            Catalog, Catalog.MerchantPrices!, s1, new SequentialRng(1UL)).DiscardPrice);
        Assert.Equal(150, MerchantInventoryGenerator.Generate(
            Catalog, Catalog.MerchantPrices!, s3, new SequentialRng(1UL)).DiscardPrice);
    }

    [Fact]
    public void Generate_CardPricesMatchRarity()
    {
        var inv = MerchantInventoryGenerator.Generate(
            Catalog, Catalog.MerchantPrices!, Base(), new SequentialRng(2UL));
        foreach (var offer in inv.Cards)
        {
            var rarity = Catalog.Cards[offer.Id].Rarity;
            Assert.Equal(Catalog.MerchantPrices!.Cards[rarity], offer.Price);
        }
    }

    [Fact]
    public void Generate_UniqueIdsWithinCategory()
    {
        var inv = MerchantInventoryGenerator.Generate(
            Catalog, Catalog.MerchantPrices!, Base(), new SequentialRng(3UL));
        Assert.Equal(inv.Cards.Length, inv.Cards.Select(c => c.Id).Distinct().Count());
        Assert.Equal(inv.Relics.Length, inv.Relics.Select(r => r.Id).Distinct().Count());
    }

    [Fact]
    public void Generate_ExcludesOwnedRelics()
    {
        var s = Base() with
        {
            Relics = Catalog.Relics.Keys.Take(Catalog.Relics.Count - 1).ToList()
        };
        var inv = MerchantInventoryGenerator.Generate(
            Catalog, Catalog.MerchantPrices!, s, new SequentialRng(4UL));
        foreach (var offer in inv.Relics)
            Assert.DoesNotContain(offer.Id, s.Relics);
    }

    [Fact]
    public void Generate_Deterministic_SameSeedSameOutput()
    {
        var a = MerchantInventoryGenerator.Generate(
            Catalog, Catalog.MerchantPrices!, Base(), new SequentialRng(99UL));
        var b = MerchantInventoryGenerator.Generate(
            Catalog, Catalog.MerchantPrices!, Base(), new SequentialRng(99UL));
        Assert.Equal(a.Cards.Select(o => o.Id), b.Cards.Select(o => o.Id));
    }

    /// <summary>
    /// Token rarity (=5) のカードは reward_ プレフィックスを持っていても
    /// 商人の在庫に並ばない。MerchantPrices.Cards に Token 価格は無いので
    /// `prices.Cards.ContainsKey(c.Rarity)` でも除外されるが、明示的な
    /// 防御フィルタで意図を明確化する (Phase 10.5.G)。
    /// </summary>
    [Fact]
    public void Generate_ExcludesTokenRarityCardsFromMerchantInventory()
    {
        var tokenCard = new CardDefinition(
            Id: "reward_token_test",
            Name: "テストトークン",
            DisplayName: null,
            Rarity: CardRarity.Token,
            CardType: CardType.Status,
            Cost: null,
            UpgradedCost: null,
            Effects: System.Array.Empty<CardEffect>(),
            UpgradedEffects: null,
            Keywords: null);
        var augmentedCards = Catalog.Cards
            .Concat(new[] { new KeyValuePair<string, CardDefinition>(tokenCard.Id, tokenCard) })
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        var augmented = Catalog with { Cards = augmentedCards };
        for (ulong seed = 1; seed < 30; seed++)
        {
            var inv = MerchantInventoryGenerator.Generate(
                augmented, augmented.MerchantPrices!, Base(), new SequentialRng(seed));
            Assert.DoesNotContain("reward_token_test", inv.Cards.Select(o => o.Id));
        }
    }

    // ---- Phase 10.6.B T4: shopPriceMultiplier helpers ----

    /// <summary>
    /// フェイクレリックを所持した RunState を生成する。
    /// Relics リストに relicId を追加するだけ。ベース状態は Base() に準ずる。
    /// </summary>
    private static RunState MakeRunStateWithRelics(DataCatalog catalog, string relicId) =>
        RunState.NewSoloRun(
            catalog, 1UL, 0,
            ImmutableDictionary<int, TileKind>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            new DateTimeOffset(2026, 4, 22, 0, 0, 0, TimeSpan.Zero))
        with { Relics = new[] { relicId } };

    /// <summary>
    /// 全レアリティ → <paramref name="perItem"/> gold、DiscardSlotPrice = <paramref name="discardSlot"/> の
    /// フラットな MerchantPrices を生成する。
    /// </summary>
    private static MerchantPrices MakeFlatMerchantPrices(int perItem, int discardSlot)
    {
        var rarities = new[] {
            CardRarity.Promo, CardRarity.Common, CardRarity.Rare,
            CardRarity.Epic, CardRarity.Legendary
        };
        var dict = rarities.ToImmutableDictionary(r => r, _ => perItem);
        return new MerchantPrices(
            Cards:  dict,
            Relics: dict,
            Potions: dict,
            DiscardSlotPrice: discardSlot);
    }

    // ---- Phase 10.6.B T4: shopPriceMultiplier tests ----

    [Fact]
    public void Generate_WithShopPriceMultiplier_DiscountsAllOffersAndDiscardPrice()
    {
        // -20% relic で全 offer + DiscardPrice が base * 80 / 100 になる
        var fake = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(Catalog,
            "loyalty",
            new[] { new CardEffect("shopPriceMultiplier", EffectScope.Self, null, -20, Trigger: "Passive") });
        var s = MakeRunStateWithRelics(fake, "loyalty");
        var prices = MakeFlatMerchantPrices(perItem: 100, discardSlot: 50);
        var rng = new SequentialRng(1UL);

        var inv = MerchantInventoryGenerator.Generate(fake, prices, s, rng);

        foreach (var offer in inv.Cards)   Assert.Equal(80, offer.Price);
        foreach (var offer in inv.Relics)  Assert.Equal(80, offer.Price);
        foreach (var offer in inv.Potions) Assert.Equal(80, offer.Price);
        Assert.Equal(40, inv.DiscardPrice); // 50 * 80 / 100
    }

    [Fact]
    public void Generate_WithExtremeNegativeMultiplier_FloorPriceAtOne()
    {
        var fake = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(Catalog,
            "extreme",
            new[] { new CardEffect("shopPriceMultiplier", EffectScope.Self, null, -200, Trigger: "Passive") });
        var s = MakeRunStateWithRelics(fake, "extreme");
        var prices = MakeFlatMerchantPrices(perItem: 100, discardSlot: 50);

        var inv = MerchantInventoryGenerator.Generate(fake, prices, s, new SequentialRng(1UL));

        foreach (var offer in inv.Cards)  Assert.Equal(1, offer.Price);
        Assert.Equal(1, inv.DiscardPrice);
    }

    [Fact]
    public void Generate_WithPositiveMultiplier_IncreasesPrice()
    {
        var fake = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(Catalog,
            "cursed_premium",
            new[] { new CardEffect("shopPriceMultiplier", EffectScope.Self, null, 30, Trigger: "Passive") });
        var s = MakeRunStateWithRelics(fake, "cursed_premium");
        var prices = MakeFlatMerchantPrices(perItem: 100, discardSlot: 50);

        var inv = MerchantInventoryGenerator.Generate(fake, prices, s, new SequentialRng(1UL));

        foreach (var offer in inv.Cards) Assert.Equal(130, offer.Price);
        Assert.Equal(65, inv.DiscardPrice); // 50 * 130 / 100
    }
}
