using System.Collections.Immutable;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Rewards;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Rewards;

public class RewardApplierTests
{
    private static RunState StateWithReward(RewardState r)
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        return TestRunStates.FreshDefault(cat) with { ActiveReward = r };
    }

    [Fact]
    public void ApplyGold_AddsGoldAndMarksClaimed()
    {
        var s = StateWithReward(new RewardState(15, false, null, true,
            ImmutableArray<string>.Empty, CardRewardStatus.Claimed));
        var next = RewardApplier.ApplyGold(s);
        Assert.Equal(s.Gold + 15, next.Gold);
        Assert.True(next.ActiveReward!.GoldClaimed);
    }

    [Fact]
    public void ApplyPotion_FullSlots_Throws()
    {
        var s = StateWithReward(new RewardState(0, true, "health_potion", false,
            ImmutableArray<string>.Empty, CardRewardStatus.Claimed));
        s = s with { Potions = ImmutableArray.Create("a", "b", "c") };
        Assert.Throws<System.InvalidOperationException>(() => RewardApplier.ApplyPotion(s));
    }

    [Fact]
    public void ApplyPotion_EmptySlot_Receives()
    {
        var s = StateWithReward(new RewardState(0, true, "health_potion", false,
            ImmutableArray<string>.Empty, CardRewardStatus.Claimed));
        s = s with { Potions = ImmutableArray.Create("a", "", "") };
        var next = RewardApplier.ApplyPotion(s);
        Assert.Equal("health_potion", next.Potions[1]);
        Assert.True(next.ActiveReward!.PotionClaimed);
    }

    [Fact]
    public void PickCard_AddsToDeckAndMarksClaimed()
    {
        var choices = ImmutableArray.Create("reward_common_01", "reward_common_02", "reward_common_03");
        var s = StateWithReward(new RewardState(0, true, null, true, choices, CardRewardStatus.Pending));
        var next = RewardApplier.PickCard(s, "reward_common_02");
        Assert.Contains("reward_common_02", next.Deck);
        Assert.Equal(CardRewardStatus.Claimed, next.ActiveReward!.CardStatus);
    }

    [Fact]
    public void PickCard_UnknownChoice_Throws()
    {
        var choices = ImmutableArray.Create("reward_common_01");
        var s = StateWithReward(new RewardState(0, true, null, true, choices, CardRewardStatus.Pending));
        Assert.Throws<System.ArgumentException>(() => RewardApplier.PickCard(s, "reward_common_99"));
    }

    [Fact]
    public void Proceed_AllComplete_ClearsActiveReward()
    {
        var s = StateWithReward(new RewardState(0, true, null, true,
            ImmutableArray<string>.Empty, CardRewardStatus.Claimed));
        var next = RewardApplier.Proceed(s);
        Assert.Null(next.ActiveReward);
    }

    [Fact]
    public void Proceed_IncompleteCard_Throws()
    {
        var choices = ImmutableArray.Create("reward_common_01", "reward_common_02", "reward_common_03");
        var s = StateWithReward(new RewardState(0, true, null, true, choices, CardRewardStatus.Pending));
        Assert.Throws<System.InvalidOperationException>(() => RewardApplier.Proceed(s));
    }

    [Fact]
    public void DiscardPotion_EmptySlot_Throws()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat) with
        { Potions = ImmutableArray.Create("health_potion", "", "") };
        Assert.Throws<System.ArgumentException>(() => RewardApplier.DiscardPotion(s, 1));
    }

    [Fact]
    public void DiscardPotion_OccupiedSlot_Empties()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat) with
        { Potions = ImmutableArray.Create("health_potion", "swift_potion", "") };
        var next = RewardApplier.DiscardPotion(s, 0);
        Assert.Equal("", next.Potions[0]);
    }
}
