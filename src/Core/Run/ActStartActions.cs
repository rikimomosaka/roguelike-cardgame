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
        if (pool.IsDefault)
            throw new InvalidOperationException($"act-start relic pool for act {act} is uninitialized");

        var configured = pool.Distinct(StringComparer.Ordinal).ToArray();
        if (configured.Length < 3)
            throw new InvalidOperationException(
                $"act {act} pool must configure at least 3 distinct relics (configured={configured.Length})");

        var owned = new HashSet<string>(state.Relics, StringComparer.Ordinal);
        var available = configured.Where(id => !owned.Contains(id)).ToList();

        var picked = new List<string>(3);
        while (picked.Count < 3 && available.Count > 0)
        {
            int idx = rng.NextInt(0, available.Count);
            picked.Add(available[idx]);
            available.RemoveAt(idx);
        }

        // Saves created before act-start relics were excluded from normal rewards
        // may already own too many entries from the current act pool. Preserve every
        // remaining dedicated option and fill only the missing slots with ordinary,
        // unowned relics so the run can safely continue without duplicates or
        // reapplying an already-owned relic's pickup effect.
        if (picked.Count < 3)
        {
            var fallback = RelicPoolHelper.GetNormalAcquisitionRelics(catalog)
                .Where(relic => !owned.Contains(relic.Id))
                .Select(relic => relic.Id)
                .ToList();
            while (picked.Count < 3 && fallback.Count > 0)
            {
                int idx = rng.NextInt(0, fallback.Count);
                picked.Add(fallback[idx]);
                fallback.RemoveAt(idx);
            }
        }

        if (picked.Count < 3)
            throw new InvalidOperationException(
                $"act {act} does not have 3 unowned relic choices across its act-start and fallback pools (available={picked.Count})");

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
        next = NonBattleRelicEffects.ApplyOnPickup(next, relicId, catalog);
        return Bestiary.BestiaryTracker.NoteRelicsAcquired(next, new[] { relicId });
    }
}
