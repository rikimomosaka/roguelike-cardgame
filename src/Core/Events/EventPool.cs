using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Events;

public static class EventPool
{
    /// <summary>
    /// act と state でフィルタリングし、rarity で重み付けしてイベントを選ぶ。
    /// - tiers に act が含まれない／event-level condition を満たさないイベントは候補外。
    /// - tiers が空のイベントは通常プールから呼ばれない。
    /// - rarity の重みは Common=3, Uncommon=2, Rare=1。
    /// </summary>
    public static EventDefinition Pick(
        ImmutableArray<EventDefinition> pool,
        int act,
        RunState state,
        IRng rng)
    {
        if (pool.IsDefault || pool.Length == 0)
            throw new InvalidOperationException("Event pool is empty");

        var candidates = pool
            .Where(d => !d.Tiers.IsDefault && d.Tiers.Contains(act))
            .Where(d => d.Condition is null || d.Condition.IsSatisfied(state))
            .OrderBy(d => d.Id, StringComparer.Ordinal)
            .ToArray();

        if (candidates.Length == 0)
            throw new InvalidOperationException(
                $"No event candidates for act={act} after filtering by tiers/condition.");

        int totalWeight = candidates.Sum(d => WeightOf(d.Rarity));
        int roll = rng.NextInt(0, totalWeight);
        int acc = 0;
        foreach (var d in candidates)
        {
            acc += WeightOf(d.Rarity);
            if (roll < acc) return d;
        }
        return candidates[^1];
    }

    private static int WeightOf(EventRarity r) => r switch
    {
        EventRarity.Common => 3,
        EventRarity.Uncommon => 2,
        EventRarity.Rare => 1,
        _ => 1,
    };
}
