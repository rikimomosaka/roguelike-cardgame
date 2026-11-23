using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Battle.Definitions;
using System.Linq;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Data;

public class EmbeddedDataLoaderTests
{
    [Fact]
    public void Embedded_cards_and_four_characters_have_approved_acquisition_metadata()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();

        Assert.Equal(4, catalog.Characters.Count);
        Assert.Equal(CharacterClass.Striker, catalog.Characters["default"].CharacterClass);
        Assert.Equal(CharacterClass.Guardian, catalog.Characters["guardian_mireille"].CharacterClass);
        Assert.Equal(CharacterClass.Tactician, catalog.Characters["tactician_yuno"].CharacterClass);
        Assert.Equal(CharacterClass.Enhancer, catalog.Characters["enhancer_lyra"].CharacterClass);
        Assert.Equal(60, catalog.Cards.Values.Count(CardPoolRules.IsReward));
        Assert.Equal(12, catalog.Cards.Values.Count(card =>
            CardPoolRules.IsReward(card) && card.PoolClass == CardPoolClass.Universal));
        foreach (var poolClass in new[]
                 {
                     CardPoolClass.Striker,
                     CardPoolClass.Guardian,
                     CardPoolClass.Tactician,
                     CardPoolClass.Enhancer,
                 })
        {
            Assert.Equal(12, catalog.Cards.Values.Count(card =>
                CardPoolRules.IsReward(card) && card.PoolClass == poolClass));
        }
        Assert.Equal(14, catalog.Cards.Values.Count(CardPoolRules.IsBasic));
        Assert.Equal(9, catalog.Cards.Values.Count(card =>
            card.EffectivePoolKinds.Count == 0 && !card.Id.StartsWith("enemy_", System.StringComparison.Ordinal)));
        // 敵の行動カードはトークンで、報酬・収集の対象外。
        foreach (var enemyCard in catalog.Cards.Values.Where(card =>
                     card.Id.StartsWith("enemy_", System.StringComparison.Ordinal)))
        {
            Assert.Equal(RoguelikeCardGame.Core.Cards.CardRarity.Token, enemyCard.Rarity);
            Assert.Empty(enemyCard.EffectivePoolKinds);
        }

        Assert.Equal(CardPoolClass.Universal, catalog.Cards["basic_attack"].PoolClass);
        Assert.Equal(CardPoolClass.Striker, catalog.Cards["striker_basic_quickdraw"].PoolClass);
        Assert.Empty(catalog.Cards["strike"].EffectivePoolKinds);
        Assert.Empty(catalog.Cards["starter_strike_2"].EffectivePoolKinds);
        Assert.Empty(catalog.Cards["starter_summon_3"].EffectivePoolKinds);
        Assert.Empty(catalog.Cards["wisp_spark"].EffectivePoolKinds);
    }

    [Fact]
    public void Override_only_card_is_added_to_runtime_catalog()
    {
        const string json = """
            {
              "id": "dev_new_card",
              "name": "Dev New Card",
              "activeVersion": "v1",
              "versions": [{
                "version": "v1",
                "spec": { "rarity": 1, "cardType": "Skill", "cost": 1, "effects": [] }
              }]
            }
            """;

        var catalog = EmbeddedDataLoader.LoadCatalogWithOverrides(
            new Dictionary<string, string> { ["dev_new_card"] = json });

        Assert.Equal("Dev New Card", catalog.Cards["dev_new_card"].Name);
    }

    [Fact]
    public void Replacement_override_for_non_versioned_content_is_used()
    {
        const string potion = """
            {
              "id": "health_potion",
              "name": "Edited Potion",
              "rarity": 1,
              "effects": [{ "action": "heal", "scope": "self", "amount": 22 }]
            }
            """;

        var catalog = EmbeddedDataLoader.LoadCatalogWithOverrides(
            new Dictionary<string, string>(),
            potionOverrides: new Dictionary<string, string> { ["health_potion"] = potion });

        Assert.Equal("Edited Potion", catalog.Potions["health_potion"].Name);
        Assert.Equal(22, Assert.Single(catalog.Potions["health_potion"].Effects).Amount);
    }

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
        Assert.Equal(9, catalog.Enemies["six_ghost"].HeightTier);
    }

    [Fact]
    public void Wisp_uses_a_permanent_fixed_cpu_deck()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var wisp = catalog.Units!["wisp"];

        Assert.True(wisp.IsCardControlled);
        Assert.Null(wisp.LifetimeTurns);
        Assert.Empty(wisp.Moves);
        Assert.Equal(UnitAiProfileIds.LowCostCombo, wisp.AiProfileId);
        Assert.Equal((1, 1, 3, 5),
            (wisp.InitialEnergy, wisp.EnergyPerTurn, wisp.DrawPerTurn, wisp.HandLimit));
        Assert.Equal(6, wisp.EffectiveDeck.Sum(entry => entry.Count));

        Assert.Collection(wisp.EffectiveDeck,
            entry => Assert.Equal(("wisp_will_o_wisp", 2), (entry.CardId, entry.Count)),
            entry => Assert.Equal(("wisp_spark", 2), (entry.CardId, entry.Count)),
            entry => Assert.Equal(("wisp_aegis", 1), (entry.CardId, entry.Count)),
            entry => Assert.Equal(("wisp_scatterflame", 1), (entry.CardId, entry.Count)));
        foreach (var entry in wisp.EffectiveDeck)
            Assert.True(catalog.Cards[entry.CardId].IsUpgradable);
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
            // 全敵カード制御へ移行済み（docs/current-enemy-card-ai-spec.md）。
            Assert.True(def.IsCardControlled, $"Enemy {id} is not card-controlled");
            Assert.Empty(def.Moves);
        }
    }

    [Fact]
    public void All_potion_JSONs_load_with_new_format()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        Assert.Equal(15, catalog.Potions.Count);
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

        var card = catalog.Cards["universal_rare_equipment_check"];
        var cardCostEffect = Assert.Single(card.Effects, e => e.Action == "modifyCardCost");
        Assert.Equal(-1, cardCostEffect.Amount);
        Assert.Equal("hand", cardCostEffect.Pile);
        Assert.Equal("choose", cardCostEffect.Select);
        Assert.Equal(1, cardCostEffect.CardCount);
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
        foreach (var (id, potion) in catalog.Potions.Where(pair => pair.Key != "health_potion"))
            Assert.False(potion.IsUsableOutsideBattle, $"{id} should not be usable outside battle");
    }

    [Fact]
    public void All_relic_JSONs_load_with_implemented_field()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        Assert.Equal(44, catalog.Relics.Count);
        // Every embedded relic is now usable; placeholders are rejected by the
        // production-data validator before the catalog reaches reward pools.
        var trueCount = catalog.Relics.Values.Count(r => r.Implemented);
        var falseCount = catalog.Relics.Values.Count(r => !r.Implemented);
        Assert.Equal(44, trueCount);
        Assert.Equal(0, falseCount);
    }

    [Fact]
    public void Added_relics_and_completed_placeholders_have_runtime_effects()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();

        Assert.Contains(catalog.Relics["iron_feather"].Effects,
            effect => effect.Action == "buff" && effect.Name == "dexterity" &&
                      effect.Trigger == "OnBattleStart" && effect.Amount == 1);
        Assert.Contains(catalog.Relics["merchant_scale"].Effects,
            effect => effect.Action == "shopPriceMultiplier" &&
                      effect.Trigger == "Passive" && effect.Amount == -15);
        Assert.Contains(catalog.Relics["ritual_chalice"].Effects,
            effect => effect.Action == "heal" &&
                      effect.Trigger == "OnEnemyDeath" && effect.Amount == 3);
        Assert.Contains(catalog.Relics["skull_mushroom"].Effects,
            effect => effect.Action == "debuff" && effect.Name == "poison" &&
                      effect.Trigger == "OnBattleStart" && effect.Amount == 2);
        Assert.Contains(catalog.Relics["honeycomb_stone"].Effects,
            effect => effect.Action == "buff" && effect.Name == "barrier" &&
                      effect.Trigger == "OnBattleStart" && effect.Amount == 2);
    }

    [Fact]
    public void Added_potions_cover_defense_offense_and_delayed_utility()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();

        Assert.Contains(catalog.Potions["barrier_potion"].Effects,
            effect => effect.Action == "buff" && effect.Name == "barrier");
        Assert.Contains(catalog.Potions["frenzy_potion"].Effects.Single().EffectiveStatusChanges(),
            change => change.Name == "strength" && change.Amount == 2);
        Assert.Contains(catalog.Potions["mastery_potion"].Effects,
            effect => effect.Action == "buff" && effect.Name == "mastery");
        Assert.Contains(catalog.Potions["revival_potion"].Effects,
            effect => effect.Action == "buff" && effect.Name == "revive" && effect.Amount == 25);
    }

    [Fact]
    public void Added_random_events_are_embedded_with_meaningful_choices()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();

        Assert.Equal(7, catalog.Events.Count);
        foreach (var id in new[] { "forgotten_forge", "moonlit_garden", "sealed_vault", "lost_caravan" })
        {
            var definition = catalog.Events[id];
            Assert.NotEmpty(definition.Tiers);
            Assert.True(definition.Choices.Length >= 3);
            Assert.Contains(definition.Choices, choice => !choice.Effects.IsEmpty);
            Assert.Contains(definition.Choices, choice => choice.Effects.IsEmpty);
        }
    }
}
