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
    public void Cost_change_trial_content_is_loaded()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();

        var card = catalog.Cards["reward_rare_10"];
        var cardCostEffect = Assert.Single(card.Effects, e => e.Action == "modifyCardCost");
        Assert.Equal(-1, cardCostEffect.Amount);
        Assert.Equal("hand", cardCostEffect.Pile);
        Assert.Equal("all", cardCostEffect.Select);
        Assert.Equal("untilPlayed", cardCostEffect.CostDuration);

        var potion = catalog.Potions["swift_potion"];
        Assert.Contains(potion.Effects, e =>
            e.Action == "buff" && e.Name == "handCostReduction" && e.Amount == 1);

        var relic = catalog.Relics["lantern"];
        Assert.True(relic.Implemented);
        Assert.Contains(relic.Effects, e =>
            e.Action == "buff" && e.Name == "handCostReduction" &&
            e.Trigger == "OnBattleStart" && e.Amount == 1);
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
        // Phase 10.6.B: lucky_die を削除 (37→36)。gamble_dice v2 で reroll 効果を移管 (false→true で +1、合計 20 維持)
        Assert.Equal(36, catalog.Relics.Count);
        // 25 files are implemented; 11 remain explicit placeholders.
        var trueCount = catalog.Relics.Values.Count(r => r.Implemented);
        var falseCount = catalog.Relics.Values.Count(r => !r.Implemented);
        Assert.Equal(25, trueCount);
        Assert.Equal(11, falseCount);
    }
}
