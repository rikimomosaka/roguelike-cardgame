using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Definitions.Loaders;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Definitions.Loaders;

public class EnemyJsonLoaderTests
{
    [Fact]
    public void Parse_minimal_enemy()
    {
        var def = EnemyJsonLoader.Parse("""
        {
          "id":"e1","name":"敵 1","imageId":"e1",
          "hp":42,"act":1,"tier":"Weak",
          "initialMoveId":"a",
          "moves":[
            {"id":"a","kind":"Attack","nextMoveId":"a",
             "effects":[{"action":"attack","scope":"all","side":"enemy","amount":5}]}
          ]
        }
        """);
        Assert.Equal("e1", def.Id);
        Assert.Equal(42, def.Hp);
        Assert.Equal(1, def.Pool.Act);
        Assert.Equal(EnemyTier.Weak, def.Pool.Tier);
        Assert.Equal("a", def.InitialMoveId);
        Assert.Single(def.Moves);
    }

    [Fact]
    public void Parse_with_multiple_moves()
    {
        var def = EnemyJsonLoader.Parse("""
        {
          "id":"e2","name":"敵 2","imageId":"e2",
          "hp":80,"act":2,"tier":"Boss",
          "initialMoveId":"a",
          "moves":[
            {"id":"a","kind":"Attack","nextMoveId":"b",
             "effects":[{"action":"attack","scope":"all","side":"enemy","amount":10}]},
            {"id":"b","kind":"Defend","nextMoveId":"a",
             "effects":[{"action":"block","scope":"self","amount":8}]}
          ]
        }
        """);
        Assert.Equal(2, def.Moves.Count);
        Assert.Equal(EnemyTier.Boss, def.Pool.Tier);
    }

    [Fact]
    public void Parse_throws_when_initialMoveId_not_in_moves()
    {
        Assert.Throws<EnemyJsonException>(() => EnemyJsonLoader.Parse("""
        {
          "id":"x","name":"x","imageId":"x",
          "hp":10,"act":1,"tier":"Weak",
          "initialMoveId":"missing",
          "moves":[
            {"id":"a","kind":"Attack","nextMoveId":"a","effects":[]}
          ]
        }
        """));
    }

    [Fact]
    public void Parse_throws_when_moves_empty()
    {
        Assert.Throws<EnemyJsonException>(() => EnemyJsonLoader.Parse("""
        {
          "id":"x","name":"x","imageId":"x",
          "hp":10,"act":1,"tier":"Weak",
          "initialMoveId":"a",
          "moves":[]
        }
        """));
    }

    [Fact]
    public void Parse_throws_when_act_out_of_range()
    {
        Assert.Throws<EnemyJsonException>(() => EnemyJsonLoader.Parse("""
        {
          "id":"x","name":"x","imageId":"x",
          "hp":10,"act":4,"tier":"Weak",
          "initialMoveId":"a",
          "moves":[{"id":"a","kind":"Attack","nextMoveId":"a","effects":[]}]
        }
        """));
    }

    [Fact]
    public void Parse_throws_when_unknown_tier()
    {
        Assert.Throws<EnemyJsonException>(() => EnemyJsonLoader.Parse("""
        {
          "id":"x","name":"x","imageId":"x",
          "hp":10,"act":1,"tier":"Mythic",
          "initialMoveId":"a",
          "moves":[{"id":"a","kind":"Attack","nextMoveId":"a","effects":[]}]
        }
        """));
    }

    [Fact]
    public void Parse_throws_on_missing_hp()
    {
        Assert.Throws<EnemyJsonException>(() => EnemyJsonLoader.Parse("""
        {
          "id":"x","name":"x","imageId":"x",
          "act":1,"tier":"Weak",
          "initialMoveId":"a",
          "moves":[{"id":"a","kind":"Attack","nextMoveId":"a","effects":[]}]
        }
        """));
    }

    [Fact]
    public void Parse_heightTier_missing_defaults_to_5()
    {
        var json = """
        {
          "id": "test", "name": "テスト", "imageId": "img", "hp": 10,
          "act": 1, "tier": "Weak", "initialMoveId": "m",
          "moves": [{"id":"m","kind":"Attack","nextMoveId":"m",
            "effects":[{"action":"attack","scope":"all","side":"enemy","amount":1}]}]
        }
        """;
        var def = EnemyJsonLoader.Parse(json);
        Assert.Equal(5, def.HeightTier);
    }

    [Fact]
    public void Parse_heightTier_value_is_preserved()
    {
        var json = """
        {
          "id": "test", "name": "テスト", "imageId": "img", "hp": 10,
          "act": 1, "tier": "Weak", "initialMoveId": "m", "heightTier": 7,
          "moves": [{"id":"m","kind":"Attack","nextMoveId":"m",
            "effects":[{"action":"attack","scope":"all","side":"enemy","amount":1}]}]
        }
        """;
        var def = EnemyJsonLoader.Parse(json);
        Assert.Equal(7, def.HeightTier);
    }

    [Fact]
    public void Parse_heightTier_below_range_throws()
    {
        var json = """
        {
          "id": "test", "name": "テスト", "imageId": "img", "hp": 10,
          "act": 1, "tier": "Weak", "initialMoveId": "m", "heightTier": 0,
          "moves": [{"id":"m","kind":"Attack","nextMoveId":"m",
            "effects":[{"action":"attack","scope":"all","side":"enemy","amount":1}]}]
        }
        """;
        Assert.Throws<EnemyJsonException>(() => EnemyJsonLoader.Parse(json));
    }

    [Fact]
    public void Parse_heightTier_above_range_throws()
    {
        var json = """
        {
          "id": "test", "name": "テスト", "imageId": "img", "hp": 10,
          "act": 1, "tier": "Weak", "initialMoveId": "m", "heightTier": 11,
          "moves": [{"id":"m","kind":"Attack","nextMoveId":"m",
            "effects":[{"action":"attack","scope":"all","side":"enemy","amount":1}]}]
        }
        """;
        Assert.Throws<EnemyJsonException>(() => EnemyJsonLoader.Parse(json));
    }

    [Fact]
    public void Parse_heightTier_non_number_throws()
    {
        var json = """
        {
          "id": "test", "name": "テスト", "imageId": "img", "hp": 10,
          "act": 1, "tier": "Weak", "initialMoveId": "m", "heightTier": "x",
          "moves": [{"id":"m","kind":"Attack","nextMoveId":"m",
            "effects":[{"action":"attack","scope":"all","side":"enemy","amount":1}]}]
        }
        """;
        Assert.Throws<EnemyJsonException>(() => EnemyJsonLoader.Parse(json));
    }
}

/// <summary>カード制御の敵（deck 形式）。</summary>
public class EnemyJsonLoaderDeckTests
{
    [Fact]
    public void Parse_card_controlled_enemy_with_deck()
    {
        var def = EnemyJsonLoader.Parse("""
        {
          "id":"e3","name":"敵 3","imageId":"e3",
          "hp":30,"act":1,"tier":"Weak",
          "energyPerTurn":2,"drawPerTurn":4,"handLimit":6,
          "aiProfileId":"low_cost_combo",
          "deck":[{"cardId":"enemy_strike","count":3},{"cardId":"enemy_guard","priority":2}]
        }
        """);
        Assert.True(def.IsCardControlled);
        Assert.Empty(def.Moves);
        Assert.Equal(string.Empty, def.InitialMoveId);
        Assert.Equal(0, def.InitialEnergy);
        Assert.Equal(2, def.EnergyPerTurn);
        Assert.Equal(4, def.DrawPerTurn);
        Assert.Equal(6, def.HandLimit);
        Assert.Equal(2, def.EffectiveDeck.Count);
        Assert.Equal(3, def.EffectiveDeck[0].Count);
        Assert.Equal(2, def.EffectiveDeck[1].Priority);
    }

    [Fact]
    public void Deck_and_moves_cannot_be_combined()
    {
        Assert.Throws<EnemyJsonException>(() => EnemyJsonLoader.Parse("""
        {
          "id":"e4","name":"敵 4","imageId":"e4",
          "hp":30,"act":1,"tier":"Weak","aiProfileId":"low_cost_combo",
          "deck":[{"cardId":"enemy_strike"}],
          "initialMoveId":"a",
          "moves":[{"id":"a","kind":"Attack","nextMoveId":"a","effects":[]}]
        }
        """));
    }

    [Fact]
    public void Deck_requires_ai_profile()
    {
        Assert.Throws<EnemyJsonException>(() => EnemyJsonLoader.Parse("""
        {
          "id":"e5","name":"敵 5","imageId":"e5",
          "hp":30,"act":1,"tier":"Weak",
          "deck":[{"cardId":"enemy_strike"}]
        }
        """));
    }

    [Fact]
    public void Description_is_optional_and_loaded_for_the_bestiary()
    {
        var withDescription = EnemyJsonLoader.Parse("""
        {
          "id":"e6","name":"敵 6","imageId":"e6",
          "hp":30,"act":2,"tier":"Elite","aiProfileId":"low_cost_combo",
          "deck":[{"cardId":"enemy_strike"}],
          "description":"洞窟に棲む。"
        }
        """);
        var without = EnemyJsonLoader.Parse("""
        {
          "id":"e7","name":"敵 7","imageId":"e7",
          "hp":30,"act":1,"tier":"Weak","aiProfileId":"low_cost_combo",
          "deck":[{"cardId":"enemy_strike"}]
        }
        """);

        Assert.Equal("洞窟に棲む。", withDescription.Description);
        Assert.Null(without.Description);
    }
}
