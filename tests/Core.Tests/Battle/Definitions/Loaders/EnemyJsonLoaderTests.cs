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
        }""");
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
        }""");
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
        }"""));
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
        }"""));
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
        }"""));
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
        }"""));
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
        }"""));
    }
}
