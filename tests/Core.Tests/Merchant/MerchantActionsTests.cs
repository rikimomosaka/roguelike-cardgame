using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Merchant;
using RoguelikeCardGame.Core.Relics;
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

    private static DataCatalog BuildCatalogWithFakeRelic(
        string id,
        IReadOnlyList<CardEffect> effects,
        bool implemented = true) =>
        RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(Catalog, id, effects, implemented);

    // MakeBuyCardState: 指定 cardId / price で商人在庫を組み立てた RunState を返す
    private static RunState MakeBuyCardState(DataCatalog catalog, string cardId, int price)
    {
        var inv = new MerchantInventory(
            Cards: ImmutableArray.Create(new MerchantOffer("card", cardId, price, false)),
            Relics: ImmutableArray<MerchantOffer>.Empty,
            Potions: ImmutableArray<MerchantOffer>.Empty,
            DiscardSlotUsed: false,
            DiscardPrice: 75);
        return RunState.NewSoloRun(
            catalog, 1UL, 0,
            ImmutableDictionary<int, TileKind>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            new DateTimeOffset(2026, 4, 22, 0, 0, 0, TimeSpan.Zero))
            with { ActiveMerchant = inv };
    }

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
    public void BuyCard_FiresOnCardAddedToDeckTrigger()
    {
        // card_collector relic: OnCardAddedToDeck で gainGold +3
        var fake = BuildCatalogWithFakeRelic(
            id: "card_collector",
            effects: new[] { new CardEffect(
                "gainGold", EffectScope.Self, null, 3, Trigger: "OnCardAddedToDeck") });
        var s0 = MakeBuyCardState(fake, cardId: "strike", price: 50) with
        {
            Gold = 100,
            Relics = new List<string> { "card_collector" }
        };

        var s1 = MerchantActions.BuyCard(s0, "strike", fake);

        // 100 - 50 (price) + 3 (relic gainGold on OnCardAddedToDeck) = 53
        Assert.Equal(53, s1.Gold);
        Assert.Contains(s1.Deck, c => c.Id == "strike");
    }

    [Fact]
    public void BuyRelic_AddsRelicAndDeductsGold()
    {
        // Phase 10.5.L1.5: relic JSON は effects=[] にリセット済みなので、
        // 購入による OnPickup 効果発火は base catalog では無く、relic 取得の
        // gold deduction だけ検証する。OnPickup 発火自体は
        // NonBattleRelicEffectsTests / RewardApplier の fake-catalog テスト側で確認。
        var s0 = BaseWithInventory(500);
        var s1 = MerchantActions.BuyRelic(s0, "extra_max_hp", Catalog);
        Assert.Contains("extra_max_hp", s1.Relics);
        Assert.Equal(350, s1.Gold);
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
    public void Leave_IsNoOp_AndPreservesInventory()
    {
        var s0 = BaseWithInventory(500);
        var s1 = MerchantActions.Leave(s0);
        Assert.Same(s0, s1);
        Assert.NotNull(s1.ActiveMerchant);
    }

    [Fact]
    public void Leave_ThenBuy_Succeeds_BecauseInventoryPersists()
    {
        var s0 = BaseWithInventory(500);
        var s1 = MerchantActions.Leave(s0);
        var s2 = MerchantActions.BuyCard(s1, "reward_common_01", Catalog);
        Assert.Equal(450, s2.Gold);
        Assert.True(s2.ActiveMerchant!.Cards[0].Sold);
    }
}
