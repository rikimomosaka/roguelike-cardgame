using System.Collections.Generic;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Rewards;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Core.Tests;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Rewards;

public class RewardActionsTests
{
    private static readonly DataCatalog BaseCatalog = EmbeddedDataLoader.LoadCatalog();

    private static RunState Sample(int gold = 50, params string[] relicIds) =>
        RunState.NewSoloRun(
            BaseCatalog, 1UL, 0,
            ImmutableDictionary<int, TileKind>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            new System.DateTimeOffset(2026, 5, 4, 0, 0, 0, System.TimeSpan.Zero)
        ) with { Gold = gold, Relics = relicIds };

    private static RewardState SampleReward(int gold = 100) =>
        new RewardState(
            Gold: gold, GoldClaimed: false,
            PotionId: null, PotionClaimed: true,
            CardChoices: ImmutableArray<string>.Empty,
            CardStatus: CardRewardStatus.Claimed);

    [Fact]
    public void AssignReward_NoRelics_ActiveRewardSetWithBaseGold()
    {
        var s0 = Sample();
        var reward = SampleReward(gold: 100);
        var s1 = RewardActions.AssignReward(s0, reward, s0.RewardRngState, BaseCatalog);
        Assert.NotNull(s1.ActiveReward);
        Assert.Equal(100, s1.ActiveReward!.Gold);
    }

    [Fact]
    public void AssignReward_WithGoldRewardMultiplier_AdjustsGold()
    {
        var fake = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(BaseCatalog,
            "lucky",
            new[] { new CardEffect("goldRewardMultiplier", EffectScope.Self, null, 50, Trigger: "Passive") });
        var s0 = Sample(gold: 0, "lucky");
        var reward = SampleReward(gold: 100);

        var s1 = RewardActions.AssignReward(s0, reward, s0.RewardRngState, fake);

        Assert.Equal(150, s1.ActiveReward!.Gold); // 100 * 1.5 = 150
    }

    [Fact]
    public void AssignReward_WithNegativeMultiplier_FloorAtZero()
    {
        var fake = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(BaseCatalog,
            "cursed",
            new[] { new CardEffect("goldRewardMultiplier", EffectScope.Self, null, -200, Trigger: "Passive") });
        var s0 = Sample(gold: 0, "cursed");
        var reward = SampleReward(gold: 100);

        var s1 = RewardActions.AssignReward(s0, reward, s0.RewardRngState, fake);

        Assert.Equal(0, s1.ActiveReward!.Gold); // -200% で床 0
    }

    [Fact]
    public void AssignReward_FiresOnRewardGeneratedTrigger()
    {
        // OnRewardGenerated relic で Gold +X が effect として走ることを確認
        var fake = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(BaseCatalog,
            "celebration",
            new[] { new CardEffect("gainGold", EffectScope.Self, null, 5, Trigger: "OnRewardGenerated") });
        var s0 = Sample(gold: 100, "celebration");
        var reward = SampleReward(gold: 50);

        var s1 = RewardActions.AssignReward(s0, reward, s0.RewardRngState, fake);

        Assert.Equal(105, s1.Gold); // 100 + 5 (trigger)
        Assert.Equal(50, s1.ActiveReward!.Gold); // reward は claim 前なので未消費
    }

    [Fact]
    public void AssignReward_NewRngStateAssigned()
    {
        var s0 = Sample();
        var newRng = s0.RewardRngState with { PotionChancePercent = 99 };
        var reward = SampleReward();
        var s1 = RewardActions.AssignReward(s0, reward, newRng, BaseCatalog);
        Assert.Equal(99, s1.RewardRngState.PotionChancePercent);
    }

    // ── Reroll テスト (Phase 10.6.B T7) ─────────────────────────────────

    [Fact]
    public void Reroll_NoActiveReward_Throws()
    {
        var s = Sample();
        Assert.Throws<System.InvalidOperationException>(() =>
            RewardActions.Reroll(s, BaseCatalog, new SequentialRng(1UL),
                new EnemyPool(1, EnemyTier.Weak),
                BaseCatalog.RewardTables["act1"]));
    }

    [Fact]
    public void Reroll_NoCapability_Throws()
    {
        var s0 = Sample();
        var reward = SampleReward(gold: 50) with {
            CardChoices = ImmutableArray.Create("strike", "defend", "bash"),
            CardStatus = CardRewardStatus.Pending,
        };
        var s1 = s0 with { ActiveReward = reward };
        Assert.Throws<System.InvalidOperationException>(() =>
            RewardActions.Reroll(s1, BaseCatalog, new SequentialRng(1UL),
                new EnemyPool(1, EnemyTier.Weak),
                BaseCatalog.RewardTables["act1"]));
    }

    [Fact]
    public void Reroll_CardAlreadyResolved_Throws()
    {
        var fake = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(BaseCatalog,
            "die", new[] { new CardEffect("rewardRerollAvailable", EffectScope.Self, null, 1, Trigger: "Passive") });
        var s0 = Sample(gold: 50, "die");
        var reward = SampleReward(gold: 50) with {
            CardChoices = ImmutableArray.Create("strike", "defend", "bash"),
            CardStatus = CardRewardStatus.Claimed, // already claimed
        };
        var s1 = s0 with { ActiveReward = reward };
        Assert.Throws<System.InvalidOperationException>(() =>
            RewardActions.Reroll(s1, fake, new SequentialRng(1UL),
                new EnemyPool(1, EnemyTier.Weak), fake.RewardTables["act1"]));
    }

    [Fact]
    public void Reroll_AlreadyUsed_Throws()
    {
        var fake = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(BaseCatalog,
            "die", new[] { new CardEffect("rewardRerollAvailable", EffectScope.Self, null, 1, Trigger: "Passive") });
        var s0 = Sample(gold: 50, "die");
        var reward = SampleReward(gold: 50) with {
            CardChoices = ImmutableArray.Create("strike", "defend", "bash"),
            CardStatus = CardRewardStatus.Pending,
            RerollUsed = true,
        };
        var s1 = s0 with { ActiveReward = reward };
        Assert.Throws<System.InvalidOperationException>(() =>
            RewardActions.Reroll(s1, fake, new SequentialRng(1UL),
                new EnemyPool(1, EnemyTier.Weak), fake.RewardTables["act1"]));
    }

    [Fact]
    public void Reroll_Successful_RegeneratesChoicesAndMarksUsed()
    {
        var fake = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(BaseCatalog,
            "die", new[] { new CardEffect("rewardRerollAvailable", EffectScope.Self, null, 1, Trigger: "Passive") });
        var s0 = Sample(gold: 50, "die");
        var oldChoices = ImmutableArray.Create("reward_common_01", "reward_common_02", "reward_common_03");
        var reward = SampleReward(gold: 50) with {
            CardChoices = oldChoices,
            CardStatus = CardRewardStatus.Pending,
            RerollUsed = false,
        };
        var s1 = s0 with { ActiveReward = reward };

        var s2 = RewardActions.Reroll(s1, fake, new SequentialRng(99UL),
            new EnemyPool(1, EnemyTier.Weak), fake.RewardTables["act1"]);

        Assert.True(s2.ActiveReward!.RerollUsed);
        Assert.Equal(3, s2.ActiveReward!.CardChoices.Length); // bonus 無しなので 3 枚
        Assert.Equal(CardRewardStatus.Pending, s2.ActiveReward!.CardStatus);
    }
}
