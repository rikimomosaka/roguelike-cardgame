using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Enemy;
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
        Assert.Equal(6, ((DamageEffect)catalog.Cards["strike"].Effects[0]).Amount);
        Assert.Equal(9, ((DamageEffect)catalog.Cards["strike"].UpgradedEffects![0]).Amount);
    }

    [Fact]
    public void LoadEmbeddedCatalog_ContainsBossEnemy()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        Assert.Contains("hexaghost", catalog.Enemies.Keys);
        Assert.Equal(EnemyTier.Boss, catalog.Enemies["hexaghost"].Pool.Tier);
    }
}
