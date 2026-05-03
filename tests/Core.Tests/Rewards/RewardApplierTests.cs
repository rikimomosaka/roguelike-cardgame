using System.Collections.Generic;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Rewards;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Rewards;

public class RewardApplierTests
{
    private static readonly DataCatalog Cat = EmbeddedDataLoader.LoadCatalog();

    // BuildCatalogWithFakeRelic helper (ローカルコピー。T9 で TestHelpers/ に集約予定)
    private static DataCatalog BuildCatalogWithFakeRelic(
        string id,
        IReadOnlyList<CardEffect> effects,
        bool implemented = true)
    {
        var fake = new RelicDefinition(
            Id: id,
            Name: $"fake_{id}",
            Rarity: CardRarity.Common,
            Effects: effects,
            Description: "",
            Implemented: implemented);

        var relics = Cat.Relics.ToDictionary(kv => kv.Key, kv => kv.Value);
        relics[id] = fake;
        return Cat with { Relics = relics };
    }

    private static RunState StateWithReward(RewardState r)
    {
        return TestRunStates.FreshDefault(Cat) with { ActiveReward = r };
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
        var next = RewardApplier.PickCard(s, "reward_common_02", Cat);
        Assert.Contains(next.Deck, ci => ci.Id == "reward_common_02");
        Assert.Equal(CardRewardStatus.Claimed, next.ActiveReward!.CardStatus);
    }

    [Fact]
    public void PickCard_UnknownChoice_Throws()
    {
        var choices = ImmutableArray.Create("reward_common_01");
        var s = StateWithReward(new RewardState(0, true, null, true, choices, CardRewardStatus.Pending));
        Assert.Throws<System.ArgumentException>(() => RewardApplier.PickCard(s, "reward_common_99", Cat));
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
    public void Proceed_IncompleteReward_StillClearsActiveReward()
    {
        // Phase 5 UX: Proceed must succeed even when gold/potion/card are unresolved,
        // so the player can always dismiss the reward popup.
        var choices = ImmutableArray.Create("reward_common_01", "reward_common_02", "reward_common_03");
        var s = StateWithReward(new RewardState(15, false, "health_potion", false, choices, CardRewardStatus.Pending));
        var next = RewardApplier.Proceed(s);
        Assert.Null(next.ActiveReward);
    }

    [Fact]
    public void PickCard_AfterSkip_AllowsClaim()
    {
        // Phase 5 UX: skipping is reversible until the player moves to the next node.
        var choices = ImmutableArray.Create("reward_common_01", "reward_common_02", "reward_common_03");
        var s = StateWithReward(new RewardState(0, true, null, true, choices, CardRewardStatus.Skipped));
        var next = RewardApplier.PickCard(s, "reward_common_02", Cat);
        Assert.Contains(next.Deck, ci => ci.Id == "reward_common_02");
        Assert.Equal(CardRewardStatus.Claimed, next.ActiveReward!.CardStatus);
    }

    [Fact]
    public void PickCard_AfterClaim_Throws()
    {
        var choices = ImmutableArray.Create("reward_common_01", "reward_common_02", "reward_common_03");
        var s = StateWithReward(new RewardState(0, true, null, true, choices, CardRewardStatus.Claimed));
        Assert.Throws<System.InvalidOperationException>(() => RewardApplier.PickCard(s, "reward_common_02", Cat));
    }

    [Fact]
    public void DiscardPotion_EmptySlot_Throws()
    {
        var s = TestRunStates.FreshDefault(Cat) with
        { Potions = ImmutableArray.Create("health_potion", "", "") };
        Assert.Throws<System.ArgumentException>(() => RewardApplier.DiscardPotion(s, 1));
    }

    [Fact]
    public void DiscardPotion_OccupiedSlot_Empties()
    {
        var s = TestRunStates.FreshDefault(Cat) with
        { Potions = ImmutableArray.Create("health_potion", "swift_potion", "") };
        var next = RewardApplier.DiscardPotion(s, 0);
        Assert.Equal("", next.Potions[0]);
    }

    [Fact]
    public void ClaimRelic_AddsRelicToRunStateAndMarksClaimed()
    {
        var s = StateWithReward(new RewardState(
            Gold: 0, GoldClaimed: true,
            PotionId: null, PotionClaimed: true,
            CardChoices: ImmutableArray<string>.Empty,
            CardStatus: CardRewardStatus.Claimed,
            RelicId: "burning_blood",
            RelicClaimed: false));
        var next = RewardApplier.ClaimRelic(s, Cat);
        Assert.Contains("burning_blood", next.Relics);
        Assert.True(next.ActiveReward!.RelicClaimed);
    }

    [Fact]
    public void ClaimRelic_AlreadyClaimed_Throws()
    {
        var s = StateWithReward(new RewardState(
            0, true, null, true,
            ImmutableArray<string>.Empty, CardRewardStatus.Claimed,
            RelicId: "burning_blood", RelicClaimed: true));
        Assert.Throws<System.InvalidOperationException>(() => RewardApplier.ClaimRelic(s, Cat));
    }

    [Fact]
    public void ClaimRelic_NoActiveReward_Throws()
    {
        var s = TestRunStates.FreshDefault(Cat);
        Assert.Throws<System.InvalidOperationException>(() => RewardApplier.ClaimRelic(s, Cat));
    }

    [Fact]
    public void ClaimRelic_NoRelicInReward_Throws()
    {
        var s = StateWithReward(new RewardState(
            0, true, null, true,
            ImmutableArray<string>.Empty, CardRewardStatus.Claimed,
            RelicId: null, RelicClaimed: true));
        Assert.Throws<System.InvalidOperationException>(() => RewardApplier.ClaimRelic(s, Cat));
    }

    [Fact]
    public void ClaimRelic_AddsRelicAndClearsRewardState()
    {
        // Phase 10.5.L1.5: extra_max_hp の base effects=[] (リセット済み) なので、
        // OnPickup 発火による MaxHp 加算は確認できない。Relics list 追加のみを検証。
        var s0 = StateWithReward(new RewardState(
            0, true, null, true,
            ImmutableArray<string>.Empty, CardRewardStatus.Claimed,
            RelicId: "extra_max_hp", RelicClaimed: false)) with
        { CurrentHp = 50, MaxHp = 80 };
        var s1 = RewardApplier.ClaimRelic(s0, Cat);
        Assert.Contains("extra_max_hp", s1.Relics);
        Assert.True(s1.ActiveReward!.RelicClaimed);
    }

    [Fact]
    public void PickCard_FiresOnCardAddedToDeckTrigger()
    {
        // OnCardAddedToDeck トリガーを持つレリックが装備されていると
        // PickCard 経由でも gainGold 効果が発火することを確認 (Phase 10.6.A T7)
        var fake = BuildCatalogWithFakeRelic(
            id: "card_collector",
            effects: new[] { new CardEffect(
                "gainGold", EffectScope.Self, null, 3, Trigger: "OnCardAddedToDeck") });
        var choices = ImmutableArray.Create("strike", "defend", "bash");
        var s0 = StateWithReward(new RewardState(0, true, null, true, choices, CardRewardStatus.Pending)) with
        {
            Gold = 50,
            Relics = new List<string> { "card_collector" }
        };

        var s1 = RewardApplier.PickCard(s0, "strike", fake);

        Assert.Equal(53, s1.Gold);  // 50 + 3 (relic gainGold on card added)
        Assert.Contains(s1.Deck, c => c.Id == "strike");
        Assert.Equal(CardRewardStatus.Claimed, s1.ActiveReward!.CardStatus);
    }
}
