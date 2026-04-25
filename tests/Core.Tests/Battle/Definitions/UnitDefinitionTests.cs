using System;
using System.Collections.Generic;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Definitions;

public class UnitDefinitionTests
{
    private static UnitDefinition Sample(int? lifetime = null) => new(
        Id: "wolf_summon",
        Name: "召喚狼",
        ImageId: "wolf",
        Hp: 12,
        InitialMoveId: "bite",
        Moves: new List<MoveDefinition>
        {
            new("bite", MoveKind.Attack,
                new[] { new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 4) },
                "bite"),
        },
        LifetimeTurns: lifetime);

    [Fact]
    public void Inherits_CombatActorDefinition()
    {
        Assert.IsAssignableFrom<CombatActorDefinition>(Sample());
    }

    [Fact]
    public void LifetimeTurns_defaults_to_null()
    {
        Assert.Null(Sample().LifetimeTurns);
    }

    [Fact]
    public void LifetimeTurns_accepts_positive_value()
    {
        Assert.Equal(3, Sample(lifetime: 3).LifetimeTurns);
    }

    [Fact]
    public void Hp_is_single_value()
    {
        Assert.Equal(12, Sample().Hp);
    }
}
