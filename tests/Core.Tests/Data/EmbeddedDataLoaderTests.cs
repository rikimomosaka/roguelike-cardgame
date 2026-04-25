using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Battle.Definitions;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Data;

public class EmbeddedDataLoaderTests
{
    [Fact]
    public void LoadEmbeddedCatalog_ContainsStrikeAndDefend()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        Assert.Contains("strike", catalog.Cards.Keys);
        Assert.Contains("defend", catalog.Cards.Keys);
        var strikeEff = catalog.Cards["strike"].Effects[0];
        Assert.Equal("attack", strikeEff.Action);
        Assert.Equal(6, strikeEff.Amount);
        var strikeUpgEff = catalog.Cards["strike"].UpgradedEffects![0];
        Assert.Equal("attack", strikeUpgEff.Action);
        Assert.Equal(9, strikeUpgEff.Amount);
    }

    [Fact]
    public void LoadEmbeddedCatalog_ContainsBossEnemy()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        Assert.Contains("six_ghost", catalog.Enemies.Keys);
        Assert.Equal(EnemyTier.Boss, catalog.Enemies["six_ghost"].Pool.Tier);
    }

    [Fact]
    public void All_enemy_JSONs_load_with_new_format()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        Assert.Equal(34, catalog.Enemies.Count);
        foreach (var (id, def) in catalog.Enemies)
        {
            Assert.NotNull(def);
            Assert.True(def.Hp > 0, $"Enemy {id} has non-positive Hp");
            Assert.NotEmpty(def.Moves);
        }
    }
}
