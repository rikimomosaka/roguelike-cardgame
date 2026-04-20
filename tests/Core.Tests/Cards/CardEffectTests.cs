using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Cards;

public class CardEffectTests
{
    [Fact]
    public void DamageEffect_HasAmount()
    {
        var e = new DamageEffect(6);
        Assert.Equal("damage", e.Type);
        Assert.Equal(6, e.Amount);
    }

    [Fact]
    public void GainBlockEffect_HasAmount()
    {
        var e = new GainBlockEffect(5);
        Assert.Equal("gainBlock", e.Type);
        Assert.Equal(5, e.Amount);
    }

    [Fact]
    public void UnknownEffect_PreservesRawType()
    {
        var e = new UnknownEffect("summonUnit");
        Assert.Equal("summonUnit", e.Type);
    }
}
