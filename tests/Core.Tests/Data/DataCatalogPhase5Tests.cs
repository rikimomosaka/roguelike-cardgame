using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Enemy;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Data;

public class DataCatalogPhase5Tests
{
    private static DataCatalog Load() => EmbeddedDataLoader.LoadCatalog();

    [Fact]
    public void Loads_DefaultCharacter()
    {
        var cat = Load();
        Assert.True(cat.TryGetCharacter("default", out var ch));
        Assert.Equal(80, ch!.MaxHp);
        Assert.Equal(99, ch.StartingGold);
        Assert.Equal(3, ch.PotionSlotCount);
        Assert.Equal(10, ch.Deck.Count);
    }

    [Fact]
    public void Loads_Act1RewardTable()
    {
        var cat = Load();
        Assert.True(cat.TryGetRewardTable("act1", out var rt));
        Assert.Equal(100, rt!.Pools[EnemyTier.Elite].PotionBasePercent);
        Assert.Equal(0, rt.Pools[EnemyTier.Boss].PotionBasePercent);
        Assert.Equal(3, rt.EnemyPoolRouting.WeakRowsThreshold);
    }

    [Fact]
    public void Encounters_AllReferencedEnemiesExist()
    {
        var cat = Load();
        Assert.NotEmpty(cat.Encounters);
        foreach (var enc in cat.Encounters.Values)
            foreach (var eid in enc.EnemyIds)
                Assert.True(cat.Enemies.ContainsKey(eid),
                    $"encounter {enc.Id} references missing enemy {eid}");
    }

    [Fact]
    public void Encounters_CoverAllFourTiers()
    {
        var cat = Load();
        Assert.Contains(cat.Encounters.Values, e => e.Pool.Tier == EnemyTier.Weak);
        Assert.Contains(cat.Encounters.Values, e => e.Pool.Tier == EnemyTier.Strong);
        Assert.Contains(cat.Encounters.Values, e => e.Pool.Tier == EnemyTier.Elite);
        Assert.Contains(cat.Encounters.Values, e => e.Pool.Tier == EnemyTier.Boss);
    }

    [Fact]
    public void RewardCards_Exist_ForAllThreeRarities()
    {
        var cat = Load();
        int common = cat.Cards.Values.Count(c => c.Id.StartsWith("reward_common_"));
        int rare   = cat.Cards.Values.Count(c => c.Id.StartsWith("reward_rare_"));
        int epic   = cat.Cards.Values.Count(c => c.Id.StartsWith("reward_epic_"));
        Assert.Equal(10, common);
        Assert.Equal(10, rare);
        Assert.Equal(10, epic);
    }

    [Fact]
    public void EnemyDefinitions_HaveInitialMoveInMoves()
    {
        var cat = Load();
        foreach (var e in cat.Enemies.Values)
            Assert.Contains(e.Moves, m => m.Id == e.InitialMoveId);
    }
}
