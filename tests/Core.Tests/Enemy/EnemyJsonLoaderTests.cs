using RoguelikeCardGame.Core.Enemy;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Enemy;

public class EnemyJsonLoaderTests
{
    [Fact]
    public void Parse_ValidJawWorm_ReturnsStateMachine()
    {
        const string json = """
        {
          "id": "jaw_worm", "name": "ジョウ・ワーム", "imageId": "jaw_worm",
          "hpMin": 40, "hpMax": 44, "act": 1, "tier": "Weak",
          "initialMoveId": "chomp",
          "moves": [
            { "id": "chomp", "kind": "attack", "damageMin": 11, "damageMax": 11, "hits": 1, "nextMoveId": "thrash" },
            { "id": "thrash", "kind": "multi", "damageMin": 7, "damageMax": 7, "hits": 1, "nextMoveId": "chomp" }
          ]
        }
        """;

        var def = EnemyJsonLoader.Parse(json);
        Assert.Equal("jaw_worm", def.Id);
        Assert.Equal("chomp", def.InitialMoveId);
        Assert.Equal(2, def.Moves.Count);
        Assert.Equal("thrash", def.Moves[1].Id);
    }

    [Fact]
    public void Parse_InitialMoveIdNotInMoves_Throws()
    {
        const string json = """
        { "id":"x","name":"x","imageId":"x","hpMin":1,"hpMax":1,"act":1,"tier":"Weak",
          "initialMoveId":"missing",
          "moves":[{"id":"a","kind":"attack","nextMoveId":"a"}] }
        """;
        Assert.Throws<EnemyJsonException>(() => EnemyJsonLoader.Parse(json));
    }

    [Fact]
    public void Parse_EmptyMoves_Throws()
    {
        const string json = """
        { "id":"x","name":"x","imageId":"x","hpMin":1,"hpMax":1,"act":1,"tier":"Weak",
          "initialMoveId":"a","moves":[] }
        """;
        Assert.Throws<EnemyJsonException>(() => EnemyJsonLoader.Parse(json));
    }
}
