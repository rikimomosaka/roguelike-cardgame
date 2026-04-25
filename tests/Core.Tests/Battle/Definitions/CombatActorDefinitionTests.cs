using System;
using System.Collections.Generic;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Definitions;

public class CombatActorDefinitionTests
{
    /// <summary>テスト用最小の派生 record。</summary>
    private sealed record TestActor(
        string Id, string Name, string ImageId, int Hp,
        string InitialMoveId, IReadOnlyList<MoveDefinition> Moves)
        : CombatActorDefinition(Id, Name, ImageId, Hp, InitialMoveId, Moves);

    [Fact]
    public void Derived_record_inherits_fields()
    {
        var moves = new List<MoveDefinition>
        {
            new("a", MoveKind.Attack, Array.Empty<CardEffect>(), "a"),
        };
        var actor = new TestActor("x", "X", "img", 30, "a", moves);
        Assert.Equal("x", actor.Id);
        Assert.Equal(30, actor.Hp);
        Assert.Equal("a", actor.InitialMoveId);
        Assert.Single(actor.Moves);
    }

    [Fact]
    public void Two_derived_records_with_same_values_are_equal()
    {
        var moves = new List<MoveDefinition>();
        var a = new TestActor("x", "X", "img", 30, "a", moves);
        var b = new TestActor("x", "X", "img", 30, "a", moves);
        Assert.Equal(a, b);
    }
}
