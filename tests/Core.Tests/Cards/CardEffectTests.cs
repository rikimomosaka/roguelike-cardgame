using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Cards;

public class CardEffectTests
{
    [Fact]
    public void Default_BattleOnly_is_false()
    {
        var e = new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5);
        Assert.False(e.BattleOnly);
    }

    [Fact]
    public void Optional_fields_default_to_null()
    {
        var e = new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5);
        Assert.Null(e.Name);
        Assert.Null(e.UnitId);
        Assert.Null(e.ComboMin);
        Assert.Null(e.Pile);
    }

    [Fact]
    public void Records_with_same_field_values_are_equal()
    {
        var a = new CardEffect("buff", EffectScope.Self, null, 1, Name: "strength");
        var b = new CardEffect("buff", EffectScope.Self, null, 1, Name: "strength");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Normalize_self_drops_side()
    {
        var e = new CardEffect("block", EffectScope.Self, EffectSide.Ally, 5);
        var n = e.Normalize();
        Assert.Null(n.Side);
    }

    [Fact]
    public void Normalize_attack_forces_side_enemy()
    {
        var e = new CardEffect("attack", EffectScope.Single, EffectSide.Ally, 5);
        var n = e.Normalize();
        Assert.Equal(EffectSide.Enemy, n.Side);
    }

    [Fact]
    public void Normalize_attack_with_null_side_forces_enemy()
    {
        var e = new CardEffect("attack", EffectScope.All, null, 5);
        var n = e.Normalize();
        Assert.Equal(EffectSide.Enemy, n.Side);
    }

    [Fact]
    public void Normalize_non_attack_with_side_keeps_side()
    {
        var e = new CardEffect("debuff", EffectScope.Single, EffectSide.Enemy, 2, Name: "vulnerable");
        var n = e.Normalize();
        Assert.Equal(EffectSide.Enemy, n.Side);
    }

    [Fact]
    public void Normalize_self_block_drops_side_even_if_specified()
    {
        var e = new CardEffect("block", EffectScope.Self, EffectSide.Ally, 5);
        var n = e.Normalize();
        Assert.Equal(EffectScope.Self, n.Scope);
        Assert.Null(n.Side);
        Assert.Equal(5, n.Amount);
    }

    [Fact]
    public void Normalize_idempotent()
    {
        var e = new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5).Normalize();
        var twice = e.Normalize();
        Assert.Equal(e, twice);
    }
}
