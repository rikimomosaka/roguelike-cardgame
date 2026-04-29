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
