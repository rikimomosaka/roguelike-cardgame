using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Rewards;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Rewards;

public class RewardGeneratorTests
{
    private static readonly ImmutableArray<string> StarterExclusions =
        ImmutableArray.Create("strike", "defend");

    private static readonly DataCatalog BaseCatalog = EmbeddedDataLoader.LoadCatalog();

    private static DataCatalog Cat() => EmbeddedDataLoader.LoadCatalog();

    /// <summary>relic 無しの基本 RunState (Phase 10.6.B T5 用)。</summary>
    private static RunState Sample() =>
        RunState.NewSoloRun(
            BaseCatalog, 1UL, 0,
            ImmutableDictionary<int, TileKind>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            new DateTimeOffset(2026, 5, 4, 0, 0, 0, TimeSpan.Zero));

    /// <summary>フェイクレリックを所持した RunState を生成する (Phase 10.6.B T5 用)。</summary>
    private static RunState MakeRunStateWithRelics(DataCatalog catalog, string relicId) =>
        RunState.NewSoloRun(
            catalog, 1UL, 0,
            ImmutableDictionary<int, TileKind>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            new DateTimeOffset(2026, 5, 4, 0, 0, 0, TimeSpan.Zero))
        with { Relics = new[] { relicId } };

    [Fact]
    public void Generate_FromWeakPool_GoldInRange()
    {
        var cat = Cat();
        var rt = cat.RewardTables["act1"];
        var ctx = new RewardContext.FromEnemy(new EnemyPool(1, EnemyTier.Weak));
        var (reward, _) = RewardGenerator.Generate(ctx, new RewardRngState(40, 0),
            StarterExclusions, rt, cat, new SystemRng(1), Sample());
        Assert.InRange(reward.Gold, rt.Pools[EnemyTier.Weak].GoldMin, rt.Pools[EnemyTier.Weak].GoldMax);
    }

    [Fact]
    public void Generate_EliteAlwaysHasPotion()
    {
        var cat = Cat();
        var rt = cat.RewardTables["act1"];
        var ctx = new RewardContext.FromEnemy(new EnemyPool(1, EnemyTier.Elite));
        for (int seed = 0; seed < 20; seed++)
        {
            var (r, _) = RewardGenerator.Generate(ctx, new RewardRngState(40, 0),
                StarterExclusions, rt, cat, new SystemRng(seed), Sample());
            Assert.NotNull(r.PotionId);
        }
    }

    [Fact]
    public void Generate_BossNeverHasPotion()
    {
        var cat = Cat();
        var rt = cat.RewardTables["act1"];
        var ctx = new RewardContext.FromEnemy(new EnemyPool(1, EnemyTier.Boss));
        for (int seed = 0; seed < 20; seed++)
        {
            var (r, _) = RewardGenerator.Generate(ctx, new RewardRngState(40, 0),
                StarterExclusions, rt, cat, new SystemRng(seed), Sample());
            Assert.Null(r.PotionId);
        }
    }

    [Fact]
    public void Generate_CardChoicesHaveNoStarterCards()
    {
        var cat = Cat();
        var rt = cat.RewardTables["act1"];
        var ctx = new RewardContext.FromEnemy(new EnemyPool(1, EnemyTier.Weak));
        for (int seed = 0; seed < 10; seed++)
        {
            var (r, _) = RewardGenerator.Generate(ctx, new RewardRngState(40, 0),
                StarterExclusions, rt, cat, new SystemRng(seed), Sample());
            Assert.Equal(3, r.CardChoices.Length);
            Assert.DoesNotContain("strike", r.CardChoices);
            Assert.DoesNotContain("defend", r.CardChoices);
            Assert.Equal(3, r.CardChoices.Distinct().Count());
        }
    }

    [Fact]
    public void Generate_NonBattle_NoCardChoices()
    {
        var cat = Cat();
        var rt = cat.RewardTables["act1"];
        var ctx = new RewardContext.FromNonBattle(NonBattleRewardKind.Event);
        var (r, _) = RewardGenerator.Generate(ctx, new RewardRngState(40, 0),
            StarterExclusions, rt, cat, new SystemRng(1), Sample());
        Assert.Empty(r.CardChoices);
        Assert.Equal(CardRewardStatus.Claimed, r.CardStatus);
    }

    [Fact]
    public void Generate_PotionDynamicChance_DecreasesOnDrop_IncreasesOnMiss()
    {
        var cat = Cat();
        var rt = cat.RewardTables["act1"];
        var ctx = new RewardContext.FromEnemy(new EnemyPool(1, EnemyTier.Weak));

        var (_, next1) = RewardGenerator.Generate(ctx, new RewardRngState(100, 0),
            StarterExclusions, rt, cat, new SystemRng(0), Sample());
        Assert.Equal(90, next1.PotionChancePercent);

        var (_, next2) = RewardGenerator.Generate(ctx, new RewardRngState(0, 0),
            StarterExclusions, rt, cat, new SystemRng(0), Sample());
        Assert.Equal(10, next2.PotionChancePercent);
    }

    [Fact]
    public void Generate_RareBonus_ResetsOnRare_IncrementsOnMiss()
    {
        var cat = Cat();
        var rt = cat.RewardTables["act1"];
        var ctx = new RewardContext.FromEnemy(new EnemyPool(1, EnemyTier.Weak));

        bool sawAny = false;
        for (int seed = 0; seed < 50; seed++)
        {
            var (r, next) = RewardGenerator.Generate(ctx, new RewardRngState(40, 0),
                StarterExclusions, rt, cat, new SystemRng(seed), Sample());
            bool hasRare = r.CardChoices.Any(id => cat.Cards[id].Rarity == CardRarity.Rare);
            if (hasRare) { Assert.Equal(0, next.RareChanceBonusPercent); sawAny = true; }
            else { Assert.Equal(1, next.RareChanceBonusPercent); }
        }
        Assert.True(sawAny, "at least one seed should produce a Rare");
    }

    [Fact]
    public void GenerateFromEnemy_Elite_AllCardsAreRareOrEpic()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var rt = catalog.RewardTables["act1"];
        var rng = new SequentialRng(1UL);
        var rngState = new RewardRngState(rt.PotionDynamic.InitialPercent, 0);
        for (int trial = 0; trial < 50; trial++)
        {
            var (reward, _) = RewardGenerator.Generate(
                new RewardContext.FromEnemy(new EnemyPool(1, EnemyTier.Elite)),
                rngState, ImmutableArray.Create("strike", "defend"), rt, catalog, rng, Sample());
            foreach (var id in reward.CardChoices)
            {
                var def = catalog.Cards[id];
                Assert.NotEqual(CardRarity.Common, def.Rarity);
            }
        }
    }

    [Fact]
    public void GenerateFromEnemy_Boss_AllCardsAreEpic()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var rt = catalog.RewardTables["act1"];
        var rng = new SequentialRng(42UL);
        var rngState = new RewardRngState(0, 0);
        var (reward, _) = RewardGenerator.Generate(
            new RewardContext.FromEnemy(new EnemyPool(1, EnemyTier.Boss)),
            rngState, ImmutableArray<string>.Empty, rt, catalog, rng, Sample());
        foreach (var id in reward.CardChoices)
        {
            Assert.Equal(CardRarity.Epic, catalog.Cards[id].Rarity);
        }
    }

    [Fact]
    public void GenerateFromNonBattle_Treasure_AlwaysYieldsGoldAndRelic_NoPotionNoCards()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var rt = catalog.RewardTables["act1"];
        var rng = new SequentialRng(3UL);
        var rngState = new RewardRngState(40, 0);
        var (reward, _) = RewardGenerator.Generate(
            new RewardContext.FromNonBattle(NonBattleRewardKind.Treasure),
            rngState, ImmutableArray<string>.Empty, rt, catalog, rng, Sample());
        var treasureEntry = rt.NonBattle["treasure"];
        Assert.InRange(reward.Gold, treasureEntry.GoldMin, treasureEntry.GoldMax);
        Assert.False(reward.GoldClaimed);
        Assert.Null(reward.PotionId);
        Assert.Empty(reward.CardChoices);
        Assert.Equal(CardRewardStatus.Claimed, reward.CardStatus);
        Assert.NotNull(reward.RelicId);
        Assert.False(reward.RelicClaimed);
    }

    [Fact]
    public void GenerateFromNonBattle_Treasure_ExcludesOwnedRelics()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var rt = catalog.RewardTables["act1"];
        var rng = new SequentialRng(99UL);
        var rngState = new RewardRngState(40, 0);
        var owned = ImmutableArray.CreateRange(catalog.Relics.Keys.Take(catalog.Relics.Count - 1));
        var (reward, _) = RewardGenerator.GenerateTreasure(rngState, owned, rt, catalog, rng);
        Assert.NotNull(reward.RelicId);
        Assert.DoesNotContain(reward.RelicId!, owned);
    }

    [Fact]
    public void GenerateFromNonBattle_Treasure_AllOwned_YieldsNullRelicAlreadyClaimed()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var rt = catalog.RewardTables["act1"];
        var rng = new SequentialRng(7UL);
        var rngState = new RewardRngState(40, 0);
        var owned = ImmutableArray.CreateRange(catalog.Relics.Keys);
        var (reward, _) = RewardGenerator.GenerateTreasure(rngState, owned, rt, catalog, rng);
        Assert.Null(reward.RelicId);
        Assert.True(reward.RelicClaimed);
    }

    /// <summary>
    /// Token rarity (=5) のカードは reward_ プレフィックスを持ち、
    /// rarity 値が Common/Rare/Epic と一致しなくても、防御的フィルタで
    /// 報酬抽選プールから除外される。これにより将来 rarity 抽選ロジックが
    /// 変わっても token カードが報酬に紛れ込まない。
    /// </summary>
    [Fact]
    public void Generate_ExcludesTokenRarityCardsFromRewards()
    {
        var baseCat = EmbeddedDataLoader.LoadCatalog();
        var tokenCard = new CardDefinition(
            Id: "reward_token_test",
            Name: "テストトークン",
            DisplayName: null,
            Rarity: CardRarity.Token,
            CardType: CardType.Status,
            Cost: null,
            UpgradedCost: null,
            Effects: System.Array.Empty<CardEffect>(),
            UpgradedEffects: null,
            Keywords: null);
        var augmentedCards = baseCat.Cards
            .Concat(new[] { new System.Collections.Generic.KeyValuePair<string, CardDefinition>(
                tokenCard.Id, tokenCard) })
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        var catalog = baseCat with { Cards = augmentedCards };
        var rt = catalog.RewardTables["act1"];
        var ctx = new RewardContext.FromEnemy(new EnemyPool(1, EnemyTier.Weak));
        for (int seed = 0; seed < 50; seed++)
        {
            var (reward, _) = RewardGenerator.Generate(ctx, new RewardRngState(40, 0),
                StarterExclusions, rt, catalog, new SystemRng(seed), Sample());
            Assert.DoesNotContain("reward_token_test", reward.CardChoices);
        }
    }

    // ---- Phase 10.6.B T5: rewardCardChoicesBonus modifier tests ----

    [Fact]
    public void GenerateFromEnemy_WithRewardCardChoicesBonus_ProducesMoreChoices()
    {
        var fake = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(BaseCatalog,
            "extra_choices",
            new[] { new CardEffect("rewardCardChoicesBonus", EffectScope.Self, null, 1, Trigger: "Passive") });
        var s = MakeRunStateWithRelics(fake, "extra_choices");

        var (reward, _) = RewardGenerator.Generate(
            new RewardContext.FromEnemy(new EnemyPool(1, EnemyTier.Weak)),
            s.RewardRngState,
            ImmutableArray<string>.Empty,
            fake.RewardTables["act1"],
            fake,
            new SequentialRng(1UL),
            s);  // 新規引数 (RunState)

        Assert.Equal(4, reward.CardChoices.Length); // 3 + 1 = 4
    }

    [Fact]
    public void GenerateFromEnemy_WithNegativeBonus_FloorAtOneChoice()
    {
        var fake = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(BaseCatalog,
            "fewer_choices",
            new[] { new CardEffect("rewardCardChoicesBonus", EffectScope.Self, null, -10, Trigger: "Passive") });
        var s = MakeRunStateWithRelics(fake, "fewer_choices");

        var (reward, _) = RewardGenerator.Generate(
            new RewardContext.FromEnemy(new EnemyPool(1, EnemyTier.Weak)),
            s.RewardRngState,
            ImmutableArray<string>.Empty,
            fake.RewardTables["act1"],
            fake,
            new SequentialRng(1UL),
            s);

        Assert.Single(reward.CardChoices); // 床 1
    }

    [Fact]
    public void RegenerateCardChoicesForReward_ProducesExpectedCount()
    {
        // T7 用に切り出した helper の単体動作確認
        var s = Sample(); // relic 無し
        var picks = RewardGenerator.RegenerateCardChoicesForReward(
            new EnemyPool(1, EnemyTier.Weak),
            s.RewardRngState,
            ImmutableArray<string>.Empty,
            BaseCatalog.RewardTables["act1"],
            BaseCatalog,
            new SequentialRng(99UL),
            s);
        Assert.Equal(3, picks.Length); // bonus 無しなので 3 枚
    }
}
