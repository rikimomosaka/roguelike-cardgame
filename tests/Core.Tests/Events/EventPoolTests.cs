using System.Collections.Immutable;
using RoguelikeCardGame.Core.Events;
using RoguelikeCardGame.Core.Random;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Events;

public class EventPoolTests
{
    private static readonly ImmutableArray<EventDefinition> Defs =
        ImmutableArray.Create(
            new EventDefinition("a", "A", "", ImmutableArray<EventChoice>.Empty),
            new EventDefinition("b", "B", "", ImmutableArray<EventChoice>.Empty),
            new EventDefinition("c", "C", "", ImmutableArray<EventChoice>.Empty));

    [Fact]
    public void Pick_DeterministicForSameSeed()
    {
        var rngA = new SequentialRng(42UL);
        var rngB = new SequentialRng(42UL);
        Assert.Equal(EventPool.Pick(Defs, rngA).Id, EventPool.Pick(Defs, rngB).Id);
    }

    [Fact]
    public void Pick_EmptyPool_Throws()
    {
        var rng = new SequentialRng(1UL);
        Assert.Throws<System.InvalidOperationException>(() =>
            EventPool.Pick(ImmutableArray<EventDefinition>.Empty, rng));
    }
}
