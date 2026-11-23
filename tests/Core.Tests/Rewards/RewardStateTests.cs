using System.Collections.Immutable;
using System.Text.Json;
using RoguelikeCardGame.Core.Json;
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
        Assert.Equal(CardRewardPoolKind.Class, r.CardPoolKind);
    }

    [Fact]
    public void Deserialize_LegacyJsonWithoutCardPoolKind_DefaultsToClass()
    {
        const string json = """
        {
          "gold": 0,
          "goldClaimed": true,
          "potionId": null,
          "potionClaimed": true,
          "cardChoices": [],
          "cardStatus": "Pending"
        }
        """;

        var reward = JsonSerializer.Deserialize<RewardState>(json, JsonOptions.Default);

        Assert.NotNull(reward);
        Assert.Equal(CardRewardPoolKind.Class, reward!.CardPoolKind);
    }
}
