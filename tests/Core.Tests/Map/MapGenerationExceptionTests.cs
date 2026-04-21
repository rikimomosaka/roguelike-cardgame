using System;
using RoguelikeCardGame.Core.Map;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Map;

public class MapGenerationExceptionTests
{
    [Fact]
    public void Constructor_SetsAttemptCountAndReason()
    {
        var ex = new MapGenerationException(100, "path-constraint:Enemy=7>6");
        Assert.Equal(100, ex.AttemptCount);
        Assert.Equal("path-constraint:Enemy=7>6", ex.FailureReason);
        Assert.Contains("path-constraint:Enemy=7>6", ex.Message);
    }

    [Fact]
    public void Constructor_WithInner_SetsInnerException()
    {
        var inner = new InvalidOperationException("boom");
        var ex = new MapGenerationException(5, "inner-failure", inner);
        Assert.Same(inner, ex.InnerException);
    }
}
