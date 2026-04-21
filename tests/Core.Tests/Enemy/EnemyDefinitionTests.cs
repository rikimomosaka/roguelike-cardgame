using RoguelikeCardGame.Core.Enemy;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Enemy;

public class EnemyDefinitionTests
{
    [Fact]
    public void Pool_EqualsBySemantic()
    {
        var a = new EnemyPool(1, EnemyTier.Weak);
        var b = new EnemyPool(1, EnemyTier.Weak);
        Assert.Equal(a, b);
    }

    [Fact]
    public void JawWorm_IsAct1Weak()
    {
        var moves = new[]
        {
            new MoveDefinition("chomp", "attack", 11, 11, 1, null, null, null, null, null, "thrash"),
            new MoveDefinition("thrash", "multi", 7, 7, 1, 5, 5, null, null, null, "bellow"),
            new MoveDefinition("bellow", "buff", null, null, null, 6, 6, "strength", 3, 5, "chomp"),
        };

        var def = new EnemyDefinition(
            Id: "jaw_worm",
            Name: "ジョウ・ワーム",
            ImageId: "jaw_worm",
            HpMin: 40,
            HpMax: 44,
            Pool: new EnemyPool(1, EnemyTier.Weak),
            InitialMoveId: "chomp",
            Moves: moves);

        Assert.Equal(1, def.Pool.Act);
        Assert.Equal(EnemyTier.Weak, def.Pool.Tier);
        Assert.Equal("chomp", def.InitialMoveId);
        Assert.Equal(3, def.Moves.Count);
    }
}
