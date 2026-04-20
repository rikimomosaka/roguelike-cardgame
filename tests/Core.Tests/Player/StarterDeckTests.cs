using System.Linq;
using RoguelikeCardGame.Core.Player;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Player;

public class StarterDeckTests
{
    [Fact]
    public void DefaultDeck_HasFiveStrikesAndFiveDefends()
    {
        var ids = StarterDeck.DefaultCardIds;
        Assert.Equal(10, ids.Count);
        Assert.Equal(5, ids.Count(i => i == "strike"));
        Assert.Equal(5, ids.Count(i => i == "defend"));
    }
}
