using System;
using System.Text.Json;
using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Cards;

public class CardEffectParserTests
{
    private static CardEffect Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return CardEffectParser.ParseEffect(doc.RootElement, msg => new Exception(msg));
    }

    [Fact]
    public void Parse_attack_single_enemy()
    {
        var e = Parse("""{"action":"attack","scope":"single","side":"enemy","amount":5}""");
        Assert.Equal("attack", e.Action);
        Assert.Equal(EffectScope.Single, e.Scope);
        Assert.Equal(EffectSide.Enemy, e.Side);
        Assert.Equal(5, e.Amount);
    }

    [Fact]
    public void Parse_block_self_drops_side_via_normalize()
    {
        var e = Parse("""{"action":"block","scope":"self","amount":6}""");
        Assert.Equal(EffectScope.Self, e.Scope);
        Assert.Null(e.Side);
        Assert.Equal(6, e.Amount);
    }

    [Fact]
    public void Parse_buff_with_name()
    {
        var e = Parse("""{"action":"buff","scope":"self","name":"strength","amount":2}""");
        Assert.Equal("buff", e.Action);
        Assert.Equal("strength", e.Name);
        Assert.Equal(2, e.Amount);
    }

    [Fact]
    public void Parse_summon_with_unitId()
    {
        var e = Parse("""{"action":"summon","scope":"self","amount":0,"unitId":"wolf"}""");
        Assert.Equal("summon", e.Action);
        Assert.Equal("wolf", e.UnitId);
    }

    [Fact]
    public void Parse_attack_with_comboMin()
    {
        var e = Parse("""{"action":"attack","scope":"single","side":"enemy","amount":5,"comboMin":2}""");
        Assert.Equal(2, e.ComboMin);
    }

    [Fact]
    public void Parse_exhaustCard_with_pile()
    {
        var e = Parse("""{"action":"exhaustCard","scope":"random","pile":"hand","amount":1}""");
        Assert.Equal("exhaustCard", e.Action);
        Assert.Equal("hand", e.Pile);
    }

    [Fact]
    public void Parse_with_battleOnly_true()
    {
        var e = Parse("""{"action":"block","scope":"self","amount":5,"battleOnly":true}""");
        Assert.True(e.BattleOnly);
    }

    [Fact]
    public void Parse_attack_normalizes_side_when_ally_specified()
    {
        var e = Parse("""{"action":"attack","scope":"single","side":"ally","amount":5}""");
        Assert.Equal(EffectSide.Enemy, e.Side);
    }

    [Fact]
    public void Parse_missing_action_throws()
    {
        Assert.Throws<Exception>(() => Parse("""{"scope":"self","amount":5}"""));
    }

    [Fact]
    public void Parse_missing_scope_throws()
    {
        Assert.Throws<Exception>(() => Parse("""{"action":"attack","amount":5}"""));
    }

    [Fact]
    public void Parse_missing_amount_throws()
    {
        Assert.Throws<Exception>(() => Parse("""{"action":"attack","scope":"self"}"""));
    }

    [Fact]
    public void Parse_unknown_scope_throws()
    {
        Assert.Throws<Exception>(() =>
            Parse("""{"action":"attack","scope":"weird","amount":5}"""));
    }

    [Fact]
    public void Parse_unknown_side_throws()
    {
        Assert.Throws<Exception>(() =>
            Parse("""{"action":"attack","scope":"single","side":"middle","amount":5}"""));
    }
}
