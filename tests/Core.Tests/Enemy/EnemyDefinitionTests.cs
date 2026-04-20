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
        var def = new EnemyDefinition(
            Id: "jaw_worm",
            Name: "ジョウ・ワーム",
            HpMin: 40,
            HpMax: 44,
            Pool: new EnemyPool(1, EnemyTier.Weak),
            Moveset: new[] { "chomp", "thrash", "bellow" });

        Assert.Equal(1, def.Pool.Act);
        Assert.Equal(EnemyTier.Weak, def.Pool.Tier);
        Assert.Equal(3, def.Moveset.Count);
    }
}
