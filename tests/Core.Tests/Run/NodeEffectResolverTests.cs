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
    public void Resolve_Rest_FullyHealsHp()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat) with { CurrentHp = 5 };
        var next = NodeEffectResolver.Resolve(s, TileKind.Rest, currentRow: 5, cat, new SystemRng(1));
        Assert.Equal(s.MaxHp, next.CurrentHp);
        Assert.Null(next.ActiveBattle);
        Assert.Null(next.ActiveReward);
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
    public void Resolve_Shop_DoesNothing()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat);
        var next = NodeEffectResolver.Resolve(s, TileKind.Merchant, currentRow: 5, cat, new SystemRng(1));
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
}
