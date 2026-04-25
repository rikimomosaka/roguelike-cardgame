using System;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class ActTransitionTests
{
    private static DungeonMap FakeMap(int startNodeId)
        => new DungeonMap(
            StartNodeId: startNodeId,
            BossNodeId: startNodeId + 100,
            Nodes: ImmutableArray.Create(new MapNode(
                Id: startNodeId, Row: 0, Column: 0,
                Kind: TileKind.Start,
                OutgoingNodeIds: ImmutableArray<int>.Empty)));

    private static DungeonMap FakeMapWithNodes(int nodeCount)
    {
        var b = ImmutableArray.CreateBuilder<MapNode>(nodeCount);
        for (int i = 0; i < nodeCount; i++)
            b.Add(new MapNode(Id: i, Row: 0, Column: i,
                Kind: i == 0 ? TileKind.Start : TileKind.Enemy,
                OutgoingNodeIds: ImmutableArray<int>.Empty));
        return new DungeonMap(StartNodeId: 0, BossNodeId: nodeCount - 1, Nodes: b.ToImmutable());
    }

    [Fact]
    public void AdvanceAct_IncrementsAct_HealsToMax_ResetsVisited()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat) with
        {
            CurrentHp = 10,
            VisitedNodeIds = ImmutableArray.Create(1, 2, 3),
            CurrentNodeId = 3,
            ActiveReward = new RoguelikeCardGame.Core.Rewards.RewardState(
                Gold: 0, GoldClaimed: true, PotionId: null, PotionClaimed: true,
                CardChoices: ImmutableArray<string>.Empty,
                CardStatus: RoguelikeCardGame.Core.Rewards.CardRewardStatus.Skipped),
        };
        var oldMap = FakeMapWithNodes(4);
        var newMap = FakeMap(999);
        var next = ActTransition.AdvanceAct(s, oldMap, newMap, cat, new SystemRng(1));
        Assert.Equal(2, next.CurrentAct);
        Assert.Equal(s.MaxHp, next.CurrentHp);
        Assert.Equal(999, next.CurrentNodeId);
        Assert.Empty(next.VisitedNodeIds);
        Assert.Null(next.ActiveReward);
        Assert.Null(next.ActiveBattle);
        Assert.Null(next.ActiveMerchant);
        Assert.Null(next.ActiveEvent);
        Assert.False(next.ActiveRestPending);
    }

    [Fact]
    public void AdvanceAct_RebuildsEncounterQueuesWithNextActEncounters()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat);
        var next = ActTransition.AdvanceAct(s, FakeMap(0), FakeMap(999), cat, new SystemRng(1));

        // After AdvanceAct from act 1 to act 2, the boss queue must contain only act 2 bosses,
        // and must not contain any act 1 boss encounter ids.
        Assert.Contains("enc_b_act2_boss", next.EncounterQueueBoss);
        Assert.DoesNotContain("enc_b_guardian", next.EncounterQueueBoss);
        Assert.DoesNotContain("enc_b_six_ghost", next.EncounterQueueBoss);
        Assert.DoesNotContain("enc_b_slime_king", next.EncounterQueueBoss);
    }

    [Fact]
    public void AdvanceAct_PreservesDeckRelicsGold()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat) with
        {
            Gold = 500,
            Relics = (IReadOnlyList<string>)new[] { "coin_purse" },
            Deck = ImmutableArray.Create(new CardInstance("strike", true)),
        };
        var next = ActTransition.AdvanceAct(s, FakeMap(0), FakeMap(1), cat, new SystemRng(1));
        Assert.Equal(500, next.Gold);
        Assert.Contains("coin_purse", next.Relics);
        Assert.Single(next.Deck);
    }

    [Fact]
    public void AdvanceAct_AppliesProvidedUnknownResolutions()
    {
        // Regression: new act map had UnknownResolutions cleared to Empty on transition,
        // causing Unknown tiles in act 2+ to throw "Unknown tile should be pre-resolved".
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat);
        var resolutions = ImmutableDictionary<int, TileKind>.Empty
            .Add(42, TileKind.Enemy)
            .Add(77, TileKind.Merchant);
        var next = ActTransition.AdvanceAct(s, FakeMap(0), FakeMap(999), cat, new SystemRng(1), resolutions);
        Assert.Equal(TileKind.Enemy, next.UnknownResolutions[42]);
        Assert.Equal(TileKind.Merchant, next.UnknownResolutions[77]);
    }

    [Fact]
    public void AdvanceAct_DefaultsUnknownResolutionsToEmpty_WhenNotProvided()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat);
        var next = ActTransition.AdvanceAct(s, FakeMap(0), FakeMap(999), cat, new SystemRng(1));
        Assert.Empty(next.UnknownResolutions);
    }

    [Fact]
    public void FinishRun_SetsProgress_AndUpdatesSavedAtUtc()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat);
        var before = s.SavedAtUtc;
        System.Threading.Thread.Sleep(10);
        var next = ActTransition.FinishRun(s, RunProgress.Cleared);
        Assert.Equal(RunProgress.Cleared, next.Progress);
        Assert.True(next.SavedAtUtc > before);
    }
}
