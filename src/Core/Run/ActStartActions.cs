using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;

namespace RoguelikeCardGame.Core.Run;

public static class ActStartActions
{
    public static ActStartRelicChoice GenerateChoices(
        RunState state, int act, DataCatalog catalog, IRng rng)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(rng);
        if (catalog.ActStartRelicPools is null || !catalog.ActStartRelicPools.TryGetValue(act, out var pool))
            throw new InvalidOperationException($"act-start relic pool for act {act} not found");

        var owned = new HashSet<string>(state.Relics, StringComparer.Ordinal);
        var available = pool.Where(id => !owned.Contains(id)).ToList();
        if (available.Count < 3)
            throw new InvalidOperationException(
                $"act {act} pool does not have 3 unowned relics (available={available.Count})");

        var picked = new List<string>(3);
        for (int i = 0; i < 3; i++)
        {
            int idx = rng.NextInt(0, available.Count);
            picked.Add(available[idx]);
            available.RemoveAt(idx);
        }
        return new ActStartRelicChoice(picked.ToImmutableArray());
    }

    public static RunState ChooseRelic(RunState state, string relicId, DataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(relicId);
        ArgumentNullException.ThrowIfNull(catalog);
        if (state.ActiveActStartRelicChoice is null)
            throw new InvalidOperationException("ActiveActStartRelicChoice is null");
        if (!state.ActiveActStartRelicChoice.RelicIds.Contains(relicId))
            throw new ArgumentException($"relicId '{relicId}' is not among current choices", nameof(relicId));

        var newRelics = state.Relics.Append(relicId).ToList();
        var next = state with
        {
            Relics = newRelics,
            ActiveActStartRelicChoice = null,
        };
        return NonBattleRelicEffects.ApplyOnPickup(next, relicId, catalog);
    }
}
