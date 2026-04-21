using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Enemy;
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
            else         { Assert.Equal(1, next.RareChanceBonusPercent); }
        }
        Assert.True(sawAny, "at least one seed should produce a Rare");
    }
}
