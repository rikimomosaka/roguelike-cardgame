using System.Collections.Generic;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;
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
    public void Resolve_Treasure_SetsActiveRewardWithGoldAndRelic()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat);
        var next = NodeEffectResolver.Resolve(s, TileKind.Treasure, 2, cat, new SequentialRng(1UL));
        Assert.NotNull(next.ActiveReward);
        var treasureEntry = cat.RewardTables["act1"].NonBattle["treasure"];
        Assert.InRange(next.ActiveReward!.Gold, treasureEntry.GoldMin, treasureEntry.GoldMax);
        Assert.False(next.ActiveReward.GoldClaimed);
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
        // 商人マスに入った状態を作り、次のマスへ進むと ActiveMerchant がクリアされることを確認
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

    [Fact]
    public void Resolve_Start_GeneratesActStartRelicChoice()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat);
        var next = NodeEffectResolver.Resolve(s, TileKind.Start, currentRow: 0, cat, new SystemRng(1));
        Assert.NotNull(next.ActiveActStartRelicChoice);
        Assert.Equal(3, next.ActiveActStartRelicChoice!.RelicIds.Length);
    }

    [Fact]
    public void Resolve_Start_UsesPoolForCurrentAct()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat) with { CurrentAct = 2 };
        var next = NodeEffectResolver.Resolve(s, TileKind.Start, currentRow: 0, cat, new SystemRng(1));
        var pool = cat.ActStartRelicPools![2];
        foreach (var id in next.ActiveActStartRelicChoice!.RelicIds)
            Assert.Contains(id, pool);
    }

    [Fact]
    public void Resolve_Merchant_FiresOnEnterShopRelicTrigger()
    {
        // Arrange: fake catalog with a "shopper" relic that grants 7 gold OnEnterShop
        var cat = BuildCatalogWithFakeRelic(
            id: "shopper",
            effects: new[] { new CardEffect(
                "gainGold", EffectScope.Self, null, 7, Trigger: "OnEnterShop") });
        var s = TestRunStates.FreshDefault(cat) with
        {
            Gold = 100,
            Relics = new List<string> { "shopper" },
        };

        // Act
        var next = NodeEffectResolver.Resolve(s, TileKind.Merchant, currentRow: 5, cat, new SystemRng(1));

        // Assert: merchant opened AND gold increased by 7
        Assert.NotNull(next.ActiveMerchant);
        Assert.Equal(107, next.Gold);
    }

    [Fact]
    public void Resolve_Rest_FiresOnEnterRestSiteRelicTrigger()
    {
        // Arrange
        var fake = BuildCatalogWithFakeRelic(
            id: "rest_camper",
            effects: new[] { new CardEffect(
                "gainGold", EffectScope.Self, null, 4, Trigger: "OnEnterRestSite") });
        var s0 = TestRunStates.FreshDefault(fake) with {
            Gold = 50,
            Relics = new List<string> { "rest_camper" }
        };
        var rng = new SequentialRng(1UL);

        // Act
        var s1 = NodeEffectResolver.Resolve(s0, TileKind.Rest, currentRow: 5, fake, rng);

        // Assert
        Assert.True(s1.ActiveRestPending);
        Assert.Equal(54, s1.Gold);
    }

    [Fact]
    public void Resolve_Treasure_FiresOnRewardGeneratedRelicTrigger()
    {
        // Arrange: fake relic that grants 11 gold OnRewardGenerated
        var fake = BuildCatalogWithFakeRelic(
            id: "lucky",
            effects: new[] { new CardEffect(
                "gainGold", EffectScope.Self, null, 11, Trigger: "OnRewardGenerated") });
        var s0 = TestRunStates.FreshDefault(fake) with
        {
            Gold = 100,
            Relics = new List<string> { "lucky" }
        };
        var rng = new SequentialRng(1UL);

        // Act
        var s1 = NodeEffectResolver.Resolve(s0, TileKind.Treasure, currentRow: 5, fake, rng);

        // Assert: reward is set AND gold increased by 11
        Assert.NotNull(s1.ActiveReward);
        Assert.Equal(111, s1.Gold);
    }

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

        var orig = EmbeddedDataLoader.LoadCatalog();
        var relics = orig.Relics.ToDictionary(kv => kv.Key, kv => kv.Value);
        relics[id] = fake;
        return orig with { Relics = relics };
    }
}
