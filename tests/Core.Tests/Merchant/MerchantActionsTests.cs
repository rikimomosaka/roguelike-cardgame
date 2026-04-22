using System;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Merchant;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Merchant;

public class MerchantActionsTests
{
    private static readonly DataCatalog Catalog = EmbeddedDataLoader.LoadCatalog();

    private static RunState BaseWithInventory(int gold = 500) =>
        (RunState.NewSoloRun(
            Catalog, 1UL, 0,
            ImmutableDictionary<int, TileKind>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            new DateTimeOffset(2026, 4, 22, 0, 0, 0, TimeSpan.Zero))
         with { Gold = gold })
        with
        { ActiveMerchant = MakeInventory() };

    private static MerchantInventory MakeInventory() => new(
        Cards: ImmutableArray.Create(
            new MerchantOffer("card", "reward_common_01", 50, false)),
        Relics: ImmutableArray.Create(
            new MerchantOffer("relic", "extra_max_hp", 150, false)),
        Potions: ImmutableArray.Create(
            new MerchantOffer("potion", "health_potion", 50, false)),
        DiscardSlotUsed: false,
        DiscardPrice: 75);

    [Fact]
    public void BuyCard_SufficientGold_AddsCardDeductsGoldMarksSold()
    {
        var s0 = BaseWithInventory(500);
        var s1 = MerchantActions.BuyCard(s0, "reward_common_01", Catalog);
        Assert.Equal(450, s1.Gold);
        Assert.Contains(s1.Deck, c => c.Id == "reward_common_01");
        Assert.True(s1.ActiveMerchant!.Cards[0].Sold);
    }

    [Fact]
    public void BuyCard_InsufficientGold_Throws()
    {
        var s0 = BaseWithInventory(30);
        Assert.Throws<InvalidOperationException>(() =>
            MerchantActions.BuyCard(s0, "reward_common_01", Catalog));
    }

    [Fact]
    public void BuyCard_AlreadySold_Throws()
    {
        var s0 = BaseWithInventory(500);
        var s1 = MerchantActions.BuyCard(s0, "reward_common_01", Catalog);
        Assert.Throws<InvalidOperationException>(() =>
            MerchantActions.BuyCard(s1, "reward_common_01", Catalog));
    }

    [Fact]
    public void BuyCard_UnknownId_Throws()
    {
        var s0 = BaseWithInventory(500);
        Assert.Throws<ArgumentException>(() =>
            MerchantActions.BuyCard(s0, "no_such_card", Catalog));
    }

    [Fact]
    public void BuyRelic_AddsRelicAndTriggersOnPickup()
    {
        var s0 = BaseWithInventory(500);
        var s1 = MerchantActions.BuyRelic(s0, "extra_max_hp", Catalog);
        Assert.Contains("extra_max_hp", s1.Relics);
        Assert.Equal(350, s1.Gold);
        Assert.Equal(s0.MaxHp + 7, s1.MaxHp);  // OnPickup 発火
    }

    [Fact]
    public void BuyPotion_AddsToFirstEmptySlot()
    {
        var s0 = BaseWithInventory(500);
        var s1 = MerchantActions.BuyPotion(s0, "health_potion", Catalog);
        Assert.Equal(450, s1.Gold);
        Assert.Equal("health_potion", s1.Potions[0]);
    }

    [Fact]
    public void BuyPotion_AllSlotsFull_Throws()
    {
        var s0 = BaseWithInventory(500);
        var full = s0 with
        {
            Potions = s0.Potions.SetItem(0, "swift_potion")
                                 .SetItem(1, "swift_potion")
                                 .SetItem(2, "swift_potion"),
        };
        Assert.Throws<InvalidOperationException>(() =>
            MerchantActions.BuyPotion(full, "health_potion", Catalog));
    }

    [Fact]
    public void Discard_RemovesCardAndMarksSlotUsed()
    {
        var s0 = BaseWithInventory(500);
        int originalLen = s0.Deck.Length;
        var s1 = MerchantActions.DiscardCard(s0, deckIndex: 0);
        Assert.Equal(originalLen - 1, s1.Deck.Length);
        Assert.Equal(425, s1.Gold);
        Assert.True(s1.ActiveMerchant!.DiscardSlotUsed);
    }

    [Fact]
    public void Discard_IncrementsDiscardUsesSoFar()
    {
        var s0 = BaseWithInventory(500);
        Assert.Equal(0, s0.DiscardUsesSoFar);
        var s1 = MerchantActions.DiscardCard(s0, 0);
        Assert.Equal(1, s1.DiscardUsesSoFar);
    }

    [Fact]
    public void Discard_AlreadyUsed_Throws()
    {
        var s0 = BaseWithInventory(500);
        var s1 = MerchantActions.DiscardCard(s0, 0);
        Assert.Throws<InvalidOperationException>(() =>
            MerchantActions.DiscardCard(s1, 0));
    }

    [Fact]
    public void Discard_InsufficientGold_Throws()
    {
        var s0 = BaseWithInventory(10);
        Assert.Throws<InvalidOperationException>(() =>
            MerchantActions.DiscardCard(s0, 0));
    }

    [Fact]
    public void Leave_SetsLeftSoFar()
    {
        var s0 = BaseWithInventory(500);
        var s1 = MerchantActions.Leave(s0);
        Assert.NotNull(s1.ActiveMerchant);
        Assert.True(s1.ActiveMerchant!.LeftSoFar);
    }

    [Fact]
    public void Leave_Twice_StillLeftSoFar()
    {
        var s0 = BaseWithInventory(500);
        var s1 = MerchantActions.Leave(s0);
        var s2 = MerchantActions.Leave(s1);
        Assert.True(s2.ActiveMerchant!.LeftSoFar);
    }

    [Fact]
    public void Leave_ThenBuy_Throws()
    {
        var s0 = BaseWithInventory(500);
        var s1 = MerchantActions.Leave(s0);
        Assert.Throws<InvalidOperationException>(() =>
            MerchantActions.BuyCard(s1, "reward_common_01", Catalog));
    }
}
