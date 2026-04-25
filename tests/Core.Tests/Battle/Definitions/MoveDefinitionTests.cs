using System;
using System.Collections.Generic;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Definitions;

public class MoveDefinitionTests
{
    [Fact]
    public void Records_with_same_field_values_are_equal()
    {
        var effects = new List<CardEffect>
        {
            new("attack", EffectScope.All, EffectSide.Enemy, 5),
        };
        var a = new MoveDefinition("m1", MoveKind.Attack, effects, "m2");
        var b = new MoveDefinition("m1", MoveKind.Attack, effects, "m2");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Empty_effects_array_is_allowed()
    {
        var def = new MoveDefinition("idle", MoveKind.Unknown, Array.Empty<CardEffect>(), "idle");
        Assert.Empty(def.Effects);
    }

    [Fact]
    public void Multiple_effects_preserve_order()
    {
        var effects = new List<CardEffect>
        {
            new("attack", EffectScope.All, EffectSide.Enemy, 7),
            new("block",  EffectScope.Self, null, 5),
        };
        var def = new MoveDefinition("thrash", MoveKind.Multi, effects, "bellow");
        Assert.Equal(2, def.Effects.Count);
        Assert.Equal("attack", def.Effects[0].Action);
        Assert.Equal("block",  def.Effects[1].Action);
    }
}
