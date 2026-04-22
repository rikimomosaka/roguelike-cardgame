using System.Collections.Immutable;
using RoguelikeCardGame.Core.Rewards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Rewards;

public class RewardStateTests
{
    [Fact]
    public void IsBossReward_DefaultsFalse()
    {
        var r = new RewardState(
            Gold: 0, GoldClaimed: true,
            PotionId: null, PotionClaimed: true,
            CardChoices: ImmutableArray<string>.Empty,
            CardStatus: CardRewardStatus.Skipped);
        Assert.False(r.IsBossReward);
    }
}
