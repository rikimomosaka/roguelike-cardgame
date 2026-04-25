using System;
using System.Collections.Generic;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Definitions;

public class EnemyDefinitionTests
{
    private static EnemyDefinition Sample(int hp = 30) => new(
        Id: "test_enemy",
        Name: "テスト敵",
        ImageId: "test",
        Hp: hp,
        Pool: new EnemyPool(1, EnemyTier.Weak),
        InitialMoveId: "m1",
        Moves: new List<MoveDefinition>
        {
            new("m1", MoveKind.Attack,
                new[] { new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 5) },
                "m1"),
        });

    [Fact]
    public void Inherits_CombatActorDefinition()
    {
        var def = Sample();
        Assert.IsAssignableFrom<CombatActorDefinition>(def);
    }

    [Fact]
    public void Hp_is_single_value()
    {
        var def = Sample(hp: 42);
        Assert.Equal(42, def.Hp);
    }

    [Fact]
    public void Pool_holds_act_and_tier()
    {
        var def = Sample();
        Assert.Equal(1, def.Pool.Act);
        Assert.Equal(EnemyTier.Weak, def.Pool.Tier);
    }

    [Fact]
    public void Records_with_same_values_are_equal()
    {
        Assert.Equal(Sample(), Sample());
    }
}
