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
}
