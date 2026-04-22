using System.Collections.Immutable;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class ActStartRelicChoiceTests
{
    [Fact]
    public void HoldsThreeRelicIds()
    {
        var c = new ActStartRelicChoice(ImmutableArray.Create("a", "b", "c"));
        Assert.Equal(3, c.RelicIds.Length);
    }
}
