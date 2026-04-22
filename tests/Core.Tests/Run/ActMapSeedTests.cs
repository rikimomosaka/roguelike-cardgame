using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class ActMapSeedTests
{
    [Fact]
    public void Deterministic_ForSameInputs()
    {
        var a = ActMapSeed.Derive(12345UL, 2);
        var b = ActMapSeed.Derive(12345UL, 2);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Differs_ForDifferentActs()
    {
        var a = ActMapSeed.Derive(12345UL, 1);
        var b = ActMapSeed.Derive(12345UL, 2);
        var c = ActMapSeed.Derive(12345UL, 3);
        Assert.NotEqual(a, b);
        Assert.NotEqual(b, c);
        Assert.NotEqual(a, c);
    }
}
