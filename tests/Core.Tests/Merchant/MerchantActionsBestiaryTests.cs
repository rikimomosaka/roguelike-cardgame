using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Merchant;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Merchant;

public class MerchantActionsBestiaryTests
{
    private static readonly DataCatalog Cat = EmbeddedDataLoader.LoadCatalog();

    [Fact]
    public void BuyRelic_TracksRelic()
    {
        var relicId = Cat.Relics.Keys.First();
        var inv = new MerchantInventory(
            Cards: ImmutableArray<MerchantOffer>.Empty,
            Relics: ImmutableArray.Create(new MerchantOffer("relic", relicId, Price: 0, Sold: false)),
            Potions: ImmutableArray<MerchantOffer>.Empty,
            DiscardSlotUsed: false, DiscardPrice: 0);
        var s = TestRunStates.FreshDefault(Cat) with { Gold = 999, ActiveMerchant = inv };
        var after = MerchantActions.BuyRelic(s, relicId, Cat);
        Assert.Contains(relicId, after.AcquiredRelicIds);
    }

    [Fact]
    public void BuyPotion_TracksPotion()
    {
        var potionId = Cat.Potions.Keys.First();
        var inv = new MerchantInventory(
            Cards: ImmutableArray<MerchantOffer>.Empty,
            Relics: ImmutableArray<MerchantOffer>.Empty,
            Potions: ImmutableArray.Create(new MerchantOffer("potion", potionId, Price: 0, Sold: false)),
            DiscardSlotUsed: false, DiscardPrice: 0);
        var s = TestRunStates.FreshDefault(Cat) with
        {
            Gold = 999,
            Potions = ImmutableArray.Create("", "", ""),
            ActiveMerchant = inv
        };
        var after = MerchantActions.BuyPotion(s, potionId, Cat);
        Assert.Contains(potionId, after.AcquiredPotionIds);
    }
}
