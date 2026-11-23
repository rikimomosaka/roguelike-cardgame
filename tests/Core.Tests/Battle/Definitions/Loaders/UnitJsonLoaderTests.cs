using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Definitions.Loaders;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Definitions.Loaders;

public class UnitJsonLoaderTests
{
    [Fact]
    public void Parse_unit_without_lifetime()
    {
        var def = UnitJsonLoader.Parse("""
        {
          "id":"wolf","name":"狼","imageId":"wolf",
          "hp":12,
          "initialMoveId":"bite",
          "moves":[
            {"id":"bite","kind":"Attack","nextMoveId":"bite",
             "effects":[{"action":"attack","scope":"single","side":"enemy","amount":4}]}
          ]
        }
        """);
        Assert.Equal("wolf", def.Id);
        Assert.Equal(12, def.Hp);
        Assert.Null(def.LifetimeTurns);
    }

    [Fact]
    public void Parse_unit_with_lifetime()
    {
        var def = UnitJsonLoader.Parse("""
        {
          "id":"spirit","name":"精霊","imageId":"spirit",
          "hp":8,
          "initialMoveId":"a",
          "moves":[{"id":"a","kind":"Attack","nextMoveId":"a","effects":[]}],
          "lifetimeTurns":3
        }
        """);
        Assert.Equal(3, def.LifetimeTurns);
    }

    [Fact]
    public void Parse_card_controlled_unit_without_moves()
    {
        var def = UnitJsonLoader.Parse("""
        {
          "id":"wisp","name":"ウィスプ","imageId":"wisp","hp":30,
          "heightTier":3,
          "initialEnergy":1,"energyPerTurn":1,"drawPerTurn":3,"handLimit":5,
          "aiProfileId":"low_cost_combo",
          "deck":[
            {"cardId":"wisp_will_o_wisp","count":2,"upgraded":false,"priority":4},
            {"cardId":"wisp_spark","count":1,"upgraded":true}
          ]
        }
        """);

        Assert.True(def.IsCardControlled);
        Assert.Empty(def.Moves);
        Assert.Equal(string.Empty, def.InitialMoveId);
        Assert.Null(def.LifetimeTurns);
        Assert.Equal(1, def.InitialEnergy);
        Assert.Equal(1, def.EnergyPerTurn);
        Assert.Equal(3, def.DrawPerTurn);
        Assert.Equal(5, def.HandLimit);
        Assert.Equal("low_cost_combo", def.AiProfileId);
        Assert.Collection(def.EffectiveDeck,
            entry =>
            {
                Assert.Equal("wisp_will_o_wisp", entry.CardId);
                Assert.Equal(2, entry.Count);
                Assert.False(entry.Upgraded);
                Assert.Equal(4, entry.Priority);
            },
            entry =>
            {
                Assert.Equal("wisp_spark", entry.CardId);
                Assert.Equal(1, entry.Count);
                Assert.True(entry.Upgraded);
                Assert.Equal(0, entry.Priority);
            });
    }

    [Fact]
    public void Parse_card_controlled_unit_uses_resource_defaults()
    {
        var def = UnitJsonLoader.Parse("""
        {
          "id":"spirit","name":"精霊","imageId":"spirit","hp":8,
          "aiProfileId":"low_cost_combo",
          "deck":[{"cardId":"spirit_attack"}]
        }
        """);

        Assert.Equal(1, def.InitialEnergy);
        Assert.Equal(1, def.EnergyPerTurn);
        Assert.Equal(3, def.DrawPerTurn);
        Assert.Equal(5, def.HandLimit);
        Assert.Equal(new UnitDeckEntry("spirit_attack"), Assert.Single(def.EffectiveDeck));
    }

    [Fact]
    public void Parse_card_controlled_unit_reads_shared_skill_and_item_loadout()
    {
        var def = UnitJsonLoader.Parse("""
        {
          "id":"spirit","name":"精霊","imageId":"spirit","hp":20,
          "aiProfileId":"low_cost_combo",
          "traitDescription":"炎を操る。",
          "deck":[{"cardId":"spirit_attack"}],
          "skills":[{
            "id":"flame_art","name":"炎術",
            "energyCost":1,"comboGaugeCost":2,"usesPerTurn":2,
            "requiresCardsInHand":true,
            "keywords":["wild"],
            "effects":[
              {"action":"buff","scope":"self","amount":1,"name":"strength"}
            ]
          }],
          "ownedRelicIds":["lantern"],
          "potions":["fire_potion","block_potion"]
        }
        """);

        Assert.Equal("炎を操る。", def.TraitDescription);
        var skill = Assert.Single(def.EffectiveSkills);
        Assert.Equal("flame_art", skill.Id);
        Assert.Equal(1, skill.Cost.Energy);
        Assert.Equal(2, skill.Cost.ComboGauge);
        Assert.Equal(2, skill.EffectiveUsesPerTurn);
        Assert.True(skill.RequiresCardsInHand);
        Assert.Equal("wild", Assert.Single(skill.Keywords!));
        Assert.Equal("strength", Assert.Single(skill.Effects).Name);
        Assert.Equal("lantern", Assert.Single(def.EffectiveOwnedRelicIds));
        Assert.Equal(new[] { "fire_potion", "block_potion" }, def.EffectivePotions);
    }

    [Fact]
    public void Parse_card_controlled_unit_rejects_duplicate_skill_ids()
    {
        var error = Assert.Throws<UnitJsonException>(() => UnitJsonLoader.Parse("""
        {
          "id":"spirit","name":"精霊","imageId":"spirit","hp":20,
          "aiProfileId":"low_cost_combo",
          "deck":[{"cardId":"spirit_attack"}],
          "skills":[
            {"id":"art","name":"一","effects":[{"action":"draw","scope":"self","amount":1}]},
            {"id":"art","name":"二","effects":[{"action":"draw","scope":"self","amount":1}]}
          ]
        }
        """));

        Assert.Contains("重複", error.Message);
    }

    [Theory]
    [InlineData("ownedRelicIds")]
    [InlineData("potions")]
    [InlineData("keywords")]
    public void Parse_card_controlled_unit_rejects_non_string_loadout_entries(string field)
    {
        string skillProperty = field == "keywords" ? ",\"keywords\":[1]" : string.Empty;
        string rootProperty = field == "keywords" ? string.Empty : $",\"{field}\":[1]";
        var json = $$"""
        {
          "id":"spirit","name":"精霊","imageId":"spirit","hp":20,
          "aiProfileId":"low_cost_combo",
          "deck":[{"cardId":"spirit_attack"}],
          "skills":[{
            "id":"art","name":"術"{{skillProperty}},
            "effects":[{"action":"draw","scope":"self","amount":1}]
          }]
          {{rootProperty}}
        }
        """;

        Assert.Throws<UnitJsonException>(() => UnitJsonLoader.Parse(json));
    }

    [Fact]
    public void Parse_throws_when_deck_and_moves_are_both_configured()
    {
        var error = Assert.Throws<UnitJsonException>(() => UnitJsonLoader.Parse("""
        {
          "id":"x","name":"x","imageId":"x","hp":10,
          "aiProfileId":"low_cost_combo",
          "deck":[{"cardId":"x_attack"}],
          "initialMoveId":"a",
          "moves":[{"id":"a","kind":"Attack","nextMoveId":"a","effects":[]}]
        }
        """));

        Assert.Contains("同時に設定", error.Message);
    }

    [Fact]
    public void Parse_throws_when_deck_is_empty()
    {
        Assert.Throws<UnitJsonException>(() => UnitJsonLoader.Parse("""
        {
          "id":"x","name":"x","imageId":"x","hp":10,
          "aiProfileId":"low_cost_combo","deck":[]
        }
        """));
    }

    [Fact]
    public void Parse_throws_when_deck_has_no_ai_profile()
    {
        Assert.Throws<UnitJsonException>(() => UnitJsonLoader.Parse("""
        {
          "id":"x","name":"x","imageId":"x","hp":10,
          "deck":[{"cardId":"x_attack"}]
        }
        """));
    }

    [Theory]
    [InlineData("initialEnergy", -1)]
    [InlineData("energyPerTurn", -1)]
    [InlineData("drawPerTurn", -1)]
    [InlineData("handLimit", 0)]
    public void Parse_throws_when_card_controlled_resource_is_out_of_range(
        string property, int value)
    {
        var json = $$"""
        {
          "id":"x","name":"x","imageId":"x","hp":10,
          "aiProfileId":"low_cost_combo",
          "deck":[{"cardId":"x_attack"}],
          "{{property}}":{{value}}
        }
        """;

        Assert.Throws<UnitJsonException>(() => UnitJsonLoader.Parse(json));
    }

    [Fact]
    public void Parse_throws_when_deck_count_is_not_positive()
    {
        Assert.Throws<UnitJsonException>(() => UnitJsonLoader.Parse("""
        {
          "id":"x","name":"x","imageId":"x","hp":10,
          "aiProfileId":"low_cost_combo",
          "deck":[{"cardId":"x_attack","count":0}]
        }
        """));
    }

    [Fact]
    public void Parse_throws_when_moves_empty()
    {
        Assert.Throws<UnitJsonException>(() => UnitJsonLoader.Parse("""
        {
          "id":"x","name":"x","imageId":"x",
          "hp":10,"initialMoveId":"a","moves":[]
        }
        """));
    }

    [Fact]
    public void Parse_throws_when_initialMoveId_not_in_moves()
    {
        Assert.Throws<UnitJsonException>(() => UnitJsonLoader.Parse("""
        {
          "id":"x","name":"x","imageId":"x",
          "hp":10,"initialMoveId":"missing",
          "moves":[{"id":"a","kind":"Attack","nextMoveId":"a","effects":[]}]
        }
        """));
    }

    [Fact]
    public void Parse_throws_on_missing_hp()
    {
        Assert.Throws<UnitJsonException>(() => UnitJsonLoader.Parse("""
        {
          "id":"x","name":"x","imageId":"x",
          "initialMoveId":"a",
          "moves":[{"id":"a","kind":"Attack","nextMoveId":"a","effects":[]}]
        }
        """));
    }

    [Fact]
    public void Parse_heightTier_missing_defaults_to_5_unit()
    {
        var json = """
        {"id":"u","name":"u","imageId":"u","hp":10,"initialMoveId":"m",
         "moves":[{"id":"m","kind":"Attack","nextMoveId":"m",
           "effects":[{"action":"attack","scope":"all","side":"enemy","amount":1}]}]}
        """;
        var def = UnitJsonLoader.Parse(json);
        Assert.Equal(5, def.HeightTier);
    }

    [Fact]
    public void Parse_heightTier_value_is_preserved_unit()
    {
        var json = """
        {"id":"u","name":"u","imageId":"u","hp":10,"initialMoveId":"m","heightTier":3,
         "moves":[{"id":"m","kind":"Attack","nextMoveId":"m",
           "effects":[{"action":"attack","scope":"all","side":"enemy","amount":1}]}]}
        """;
        var def = UnitJsonLoader.Parse(json);
        Assert.Equal(3, def.HeightTier);
    }

    [Fact]
    public void Parse_heightTier_below_range_throws_unit()
    {
        var json = """
        {"id":"u","name":"u","imageId":"u","hp":10,"initialMoveId":"m","heightTier":0,
         "moves":[{"id":"m","kind":"Attack","nextMoveId":"m",
           "effects":[{"action":"attack","scope":"all","side":"enemy","amount":1}]}]}
        """;
        Assert.Throws<UnitJsonException>(() => UnitJsonLoader.Parse(json));
    }

    [Fact]
    public void Parse_heightTier_above_range_throws_unit()
    {
        var json = """
        {"id":"u","name":"u","imageId":"u","hp":10,"initialMoveId":"m","heightTier":11,
         "moves":[{"id":"m","kind":"Attack","nextMoveId":"m",
           "effects":[{"action":"attack","scope":"all","side":"enemy","amount":1}]}]}
        """;
        Assert.Throws<UnitJsonException>(() => UnitJsonLoader.Parse(json));
    }

    [Fact]
    public void Parse_heightTier_non_number_throws_unit()
    {
        var json = """
        {"id":"u","name":"u","imageId":"u","hp":10,"initialMoveId":"m","heightTier":"x",
         "moves":[{"id":"m","kind":"Attack","nextMoveId":"m",
           "effects":[{"action":"attack","scope":"all","side":"enemy","amount":1}]}]}
        """;
        Assert.Throws<UnitJsonException>(() => UnitJsonLoader.Parse(json));
    }
}
