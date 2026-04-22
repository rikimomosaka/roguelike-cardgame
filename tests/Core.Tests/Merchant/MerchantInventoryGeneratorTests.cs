using System;
using System.Collections.Immutable;
using System.Linq;
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
}
