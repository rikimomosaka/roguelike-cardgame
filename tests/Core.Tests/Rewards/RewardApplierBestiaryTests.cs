using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Rewards;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Rewards;

public class RewardApplierBestiaryTests
{
    private static readonly DataCatalog Cat = EmbeddedDataLoader.LoadCatalog();

    [Fact]
    public void ApplyPotion_TracksPotion()
    {
        var s = TestRunStates.FreshDefault(Cat) with
        {
            Potions = ImmutableArray.Create("", "", ""),
            ActiveReward = new RewardState(
                Gold: 0, GoldClaimed: true,
                PotionId: "fire_potion", PotionClaimed: false,
                CardChoices: ImmutableArray<string>.Empty,
                CardStatus: CardRewardStatus.Claimed),
        };
        var after = RewardApplier.ApplyPotion(s);
        Assert.Contains("fire_potion", after.AcquiredPotionIds);
    }

    [Fact]
    public void ClaimRelic_TracksRelic()
    {
        var relicId = Cat.Relics.Keys.First();
        var s = TestRunStates.FreshDefault(Cat) with
        {
            ActiveReward = new RewardState(
                Gold: 0, GoldClaimed: true,
                PotionId: null, PotionClaimed: true,
                CardChoices: ImmutableArray<string>.Empty,
                CardStatus: CardRewardStatus.Claimed,
                RelicId: relicId,
                RelicClaimed: false),
        };
        var after = RewardApplier.ClaimRelic(s, Cat);
        Assert.Contains(relicId, after.AcquiredRelicIds);
    }
}
