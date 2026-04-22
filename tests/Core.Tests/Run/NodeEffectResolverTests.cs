using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Enemy;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class NodeEffectResolverTests
{
    private static RunState FreshWithQueues(DataCatalog cat)
    {
        var s = TestRunStates.FreshDefault(cat);
        var rng = new SystemRng(1);
        return s with
        {
            EncounterQueueWeak = EncounterQueue.Initialize(
                new EnemyPool(s.CurrentAct, EnemyTier.Weak), cat, rng),
            EncounterQueueStrong = EncounterQueue.Initialize(
                new EnemyPool(s.CurrentAct, EnemyTier.Strong), cat, rng),
            EncounterQueueElite = EncounterQueue.Initialize(
                new EnemyPool(s.CurrentAct, EnemyTier.Elite), cat, rng),
            EncounterQueueBoss = EncounterQueue.Initialize(
                new EnemyPool(s.CurrentAct, EnemyTier.Boss), cat, rng),
        };
    }

    [Fact]
    public void Resolve_Enemy_WeakRow_StartsBattleWithWeakPool()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = FreshWithQueues(cat);
        var next = NodeEffectResolver.Resolve(s, TileKind.Enemy, currentRow: 2, cat, new SystemRng(1));
        Assert.NotNull(next.ActiveBattle);
    }

    [Fact]
    public void Resolve_Rest_SetsActiveRestPending()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat) with { CurrentHp = 40 };
        var next = NodeEffectResolver.Resolve(s, TileKind.Rest, currentRow: 2, cat, new SequentialRng(1UL));
        Assert.True(next.ActiveRestPending);
        Assert.Equal(40, next.CurrentHp);  // Rest 選択まで回復しない
    }

    [Fact]
    public void Resolve_Treasure_CreatesActiveReward_NoCards()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat);
        var next = NodeEffectResolver.Resolve(s, TileKind.Treasure, currentRow: 5, cat, new SystemRng(1));
        Assert.NotNull(next.ActiveReward);
        Assert.Empty(next.ActiveReward!.CardChoices);
    }

    [Fact]
    public void Resolve_Treasure_SetsActiveRewardWithRelicOnly()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat);
        var next = NodeEffectResolver.Resolve(s, TileKind.Treasure, 2, cat, new SequentialRng(1UL));
        Assert.NotNull(next.ActiveReward);
        Assert.Equal(0, next.ActiveReward!.Gold);
        Assert.Empty(next.ActiveReward.CardChoices);
        Assert.NotNull(next.ActiveReward.RelicId);
        Assert.False(next.ActiveReward.RelicClaimed);
    }

    [Fact]
    public void Resolve_Event_SetsActiveEvent()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat);
        var next = NodeEffectResolver.Resolve(s, TileKind.Event, currentRow: 2, cat, new SequentialRng(1UL));
        Assert.NotNull(next.ActiveEvent);
        Assert.Contains(next.ActiveEvent!.EventId, cat.Events.Keys);
    }

    [Fact]
    public void Resolve_Merchant_SetsActiveMerchant()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat);
        var next = NodeEffectResolver.Resolve(s, TileKind.Merchant, currentRow: 5, cat, new SystemRng(1));
        Assert.NotNull(next.ActiveMerchant);
        Assert.Null(next.ActiveBattle);
        Assert.Null(next.ActiveReward);
        Assert.Equal(s.CurrentHp, next.CurrentHp);
    }

    [Fact]
    public void Resolve_Boss_StartsBattleWithBossPool()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = FreshWithQueues(cat);
        var next = NodeEffectResolver.Resolve(s, TileKind.Boss, currentRow: 15, cat, new SystemRng(1));
        Assert.NotNull(next.ActiveBattle);
    }

    [Fact]
    public void Resolve_ClearsOldActiveMerchant()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        // 商人マスで Leave した状態（LeftSoFar=true）を持つ state を作る
        var s = TestRunStates.FreshDefault(cat);
        var withMerchant = NodeEffectResolver.Resolve(s, TileKind.Merchant, currentRow: 5, cat, new SystemRng(1));
        Assert.NotNull(withMerchant.ActiveMerchant);
        // 次のマスへ移動（Rest）
        var next = NodeEffectResolver.Resolve(withMerchant, TileKind.Rest, currentRow: 6, cat, new SequentialRng(1UL));
        Assert.Null(next.ActiveMerchant);
        Assert.True(next.ActiveRestPending);
    }

    [Fact]
    public void Resolve_ClearsActiveRestCompleted()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat) with
        {
            ActiveRestPending = true,
            ActiveRestCompleted = true,
        };
        // 次のマスへ移動
        var next = NodeEffectResolver.Resolve(s, TileKind.Event, currentRow: 7, cat, new SequentialRng(1UL));
        Assert.False(next.ActiveRestPending);
        Assert.False(next.ActiveRestCompleted);
        Assert.NotNull(next.ActiveEvent);
    }
}
