using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Data;

namespace RoguelikeCardGame.Core.Relics;

/// <summary>
/// Centralizes the boundary between act-start-only relics and relics that may
/// appear through ordinary acquisition routes.
/// </summary>
public static class RelicPoolHelper
{
    /// <summary>
    /// Returns every relic ID reserved by any configured act-start pool.
    /// The data catalog is authoritative; relic ID naming is intentionally ignored.
    /// </summary>
    public static ImmutableHashSet<string> GetActStartReservedIds(DataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var reserved = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        if (catalog.ActStartRelicPools is null)
            return reserved.ToImmutable();

        foreach (var pool in catalog.ActStartRelicPools.Values)
        {
            if (pool.IsDefault) continue;
            foreach (var relicId in pool)
            {
                if (relicId is not null)
                    reserved.Add(relicId);
            }
        }

        return reserved.ToImmutable();
    }

    /// <summary>
    /// Returns the deterministic pool available to merchants, treasures, and
    /// ordinary event rewards.
    /// </summary>
    public static ImmutableArray<RelicDefinition> GetNormalAcquisitionRelics(DataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var reserved = GetActStartReservedIds(catalog);
        return catalog.Relics.Values
            .Where(relic => !reserved.Contains(relic.Id))
            .OrderBy(relic => relic.Id, StringComparer.Ordinal)
            .ToImmutableArray();
    }
}
