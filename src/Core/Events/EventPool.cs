using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Events;

public static class EventPool
{
    public static EventDefinition Pick(ImmutableArray<EventDefinition> pool, IRng rng)
    {
        if (pool.IsDefault || pool.Length == 0)
            throw new InvalidOperationException("Event pool is empty");
        var sorted = pool.OrderBy(d => d.Id).ToArray();
        return sorted[rng.NextInt(0, sorted.Length)];
    }
}
