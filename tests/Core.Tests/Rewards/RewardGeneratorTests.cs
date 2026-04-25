using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Rewards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Rewards;

public class RewardGeneratorTests
{
    private static readonly ImmutableArray<string> StarterExclusions =
        ImmutableArray.Create("strike", "defend");

    private static DataCatalog Cat() => EmbeddedDataLoader.LoadCatalog();

    [Fact]
    public void Generate_FromWeakPool_GoldInRange()
    {
        var cat = Cat();
        var rt = cat.RewardTables["act1"];
        var ctx = new RewardContext.FromEnemy(new EnemyPool(1, EnemyTier.Weak));
        var (reward, _) = RewardGenerator.Generate(ctx, new RewardRngState(40, 0),
            StarterExclusions, rt, cat, new SystemRng(1));
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
                StarterExclusions, rt, cat, new SystemRng(seed));
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
                StarterExclusions, rt, cat, new SystemRng(seed));
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
                StarterExclusions, rt, cat, new SystemRng(seed));
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
            StarterExclusions, rt, cat, new SystemRng(1));
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
            StarterExclusions, rt, cat, new SystemRng(0));
        Assert.Equal(90, next1.PotionChancePercent);

        var (_, next2) = RewardGenerator.Generate(ctx, new RewardRngState(0, 0),
            StarterExclusions, rt, cat, new SystemRng(0));
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
                StarterExclusions, rt, cat, new SystemRng(seed));
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
                rngState, ImmutableArray.Create("strike", "defend"), rt, catalog, rng);
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
            rngState, ImmutableArray<string>.Empty, rt, catalog, rng);
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
            rngState, ImmutableArray<string>.Empty, rt, catalog, rng);
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
}
