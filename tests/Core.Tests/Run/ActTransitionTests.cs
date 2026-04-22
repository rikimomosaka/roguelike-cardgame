using System;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Enemy;
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
        var newMap = FakeMap(999);
        var next = ActTransition.AdvanceAct(s, newMap, cat, new SystemRng(1));
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
    public void AdvanceAct_PreservesDeckRelicsGold()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat) with
        {
            Gold = 500,
            Relics = (IReadOnlyList<string>)new[] { "coin_purse" },
            Deck = ImmutableArray.Create(new CardInstance("strike", true)),
        };
        var next = ActTransition.AdvanceAct(s, FakeMap(1), cat, new SystemRng(1));
        Assert.Equal(500, next.Gold);
        Assert.Contains("coin_purse", next.Relics);
        Assert.Single(next.Deck);
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
