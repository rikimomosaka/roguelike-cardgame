using System;
using System.Text.Json;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Definitions.Loaders;
using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Definitions.Loaders;

public class MoveJsonLoaderTests
{
    private static MoveDefinition Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return MoveJsonLoader.ParseMove(doc.RootElement, msg => new Exception(msg));
    }

    [Fact]
    public void Parse_attack_move_with_one_effect()
    {
        var m = Parse("""
        {"id":"chomp","kind":"Attack","nextMoveId":"thrash",
         "effects":[{"action":"attack","scope":"all","side":"enemy","amount":11}]}
        """);
        Assert.Equal("chomp", m.Id);
        Assert.Equal(MoveKind.Attack, m.Kind);
        Assert.Equal("thrash", m.NextMoveId);
        Assert.Single(m.Effects);
        Assert.Equal("attack", m.Effects[0].Action);
        Assert.Equal(11, m.Effects[0].Amount);
    }

    [Fact]
    public void Parse_multi_move_with_multiple_effects()
    {
        var m = Parse("""
        {"id":"thrash","kind":"Multi","nextMoveId":"bellow",
         "effects":[
           {"action":"attack","scope":"all","side":"enemy","amount":7},
           {"action":"block","scope":"self","amount":5}
         ]}
        """);
        Assert.Equal(MoveKind.Multi, m.Kind);
        Assert.Equal(2, m.Effects.Count);
        Assert.Equal("attack", m.Effects[0].Action);
        Assert.Equal("block",  m.Effects[1].Action);
    }

    [Fact]
    public void Parse_defend_kind()
    {
        var m = Parse("""
        {"id":"d","kind":"Defend","nextMoveId":"d","effects":[]}
        """);
        Assert.Equal(MoveKind.Defend, m.Kind);
    }

    [Fact]
    public void Parse_buff_kind()
    {
        var m = Parse("""
        {"id":"b","kind":"Buff","nextMoveId":"b","effects":[]}
        """);
        Assert.Equal(MoveKind.Buff, m.Kind);
    }

    [Fact]
    public void Parse_debuff_kind()
    {
        var m = Parse("""
        {"id":"x","kind":"Debuff","nextMoveId":"x","effects":[]}
        """);
        Assert.Equal(MoveKind.Debuff, m.Kind);
    }

    [Fact]
    public void Parse_heal_kind()
    {
        var m = Parse("""
        {"id":"h","kind":"Heal","nextMoveId":"h","effects":[]}
        """);
        Assert.Equal(MoveKind.Heal, m.Kind);
    }

    [Fact]
    public void Parse_unknown_kind()
    {
        var m = Parse("""
        {"id":"u","kind":"Unknown","nextMoveId":"u","effects":[]}
        """);
        Assert.Equal(MoveKind.Unknown, m.Kind);
    }

    [Fact]
    public void Parse_empty_effects_array()
    {
        var m = Parse("""
        {"id":"idle","kind":"Unknown","nextMoveId":"idle","effects":[]}
        """);
        Assert.Empty(m.Effects);
    }

    [Fact]
    public void Parse_missing_effects_throws()
    {
        Assert.Throws<Exception>(() =>
            Parse("""{"id":"x","kind":"Attack","nextMoveId":"x"}"""));
    }

    [Fact]
    public void Parse_unknown_kind_throws()
    {
        Assert.Throws<Exception>(() =>
            Parse("""{"id":"x","kind":"Weird","nextMoveId":"x","effects":[]}"""));
    }

    [Fact]
    public void Parse_missing_id_throws()
    {
        Assert.Throws<Exception>(() =>
            Parse("""{"kind":"Attack","nextMoveId":"x","effects":[]}"""));
    }

    [Fact]
    public void Parse_missing_nextMoveId_throws()
    {
        Assert.Throws<Exception>(() =>
            Parse("""{"id":"x","kind":"Attack","effects":[]}"""));
    }
}
