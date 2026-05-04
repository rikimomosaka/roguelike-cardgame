using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Battle.Definitions;
using System.Linq;
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

    [Fact]
    public void All_potion_JSONs_load_with_new_format()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        Assert.Equal(7, catalog.Potions.Count);
        foreach (var (id, def) in catalog.Potions)
        {
            Assert.NotNull(def);
            Assert.NotEmpty(def.Effects);
        }
    }

    [Fact]
    public void HealthPotion_IsUsableOutsideBattle()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        Assert.True(catalog.Potions["health_potion"].IsUsableOutsideBattle);
    }

    [Fact]
    public void NonHealthPotions_AreNotUsableOutsideBattle()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var nonHealth = new[] { "block_potion", "energy_potion", "fire_potion",
                                "poison_potion", "strength_potion", "swift_potion" };
        foreach (var id in nonHealth)
            Assert.False(catalog.Potions[id].IsUsableOutsideBattle, $"{id} should not be usable outside battle");
    }

    [Fact]
    public void All_relic_JSONs_load_with_implemented_field()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        Assert.Equal(37, catalog.Relics.Count);  // +1 lucky_die (Phase 10.6.B T7)
        // 20 ファイルが Implemented: true、17 ファイルが Implemented: false の想定
        var trueCount = catalog.Relics.Values.Count(r => r.Implemented);
        var falseCount = catalog.Relics.Values.Count(r => !r.Implemented);
        Assert.Equal(20, trueCount);
        Assert.Equal(17, falseCount);
    }
}
