using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Cards;

public class CardInstanceTests
{
    [Fact]
    public void Constructor_DefaultsUpgradedFalse()
    {
        var ci = new CardInstance("strike");
        Assert.Equal("strike", ci.Id);
        Assert.False(ci.Upgraded);
    }

    [Fact]
    public void WithExpression_TogglesUpgraded()
    {
        var ci = new CardInstance("strike") with { Upgraded = true };
        Assert.True(ci.Upgraded);
    }

    [Fact]
    public void Equality_ByValue()
    {
        var a = new CardInstance("strike", false);
        var b = new CardInstance("strike", false);
        Assert.Equal(a, b);
    }
}
