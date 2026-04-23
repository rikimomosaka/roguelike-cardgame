using System;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.History;

namespace RoguelikeCardGame.Core.Bestiary;

public static class BestiaryUpdater
{
    public static BestiaryState Merge(BestiaryState current, RunHistoryRecord record)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(record);
        return new BestiaryState(
            SchemaVersion: BestiaryState.CurrentSchemaVersion,
            DiscoveredCardBaseIds: Union(current.DiscoveredCardBaseIds, record.SeenCardBaseIds),
            DiscoveredRelicIds: Union(current.DiscoveredRelicIds, record.AcquiredRelicIds),
            DiscoveredPotionIds: Union(current.DiscoveredPotionIds, record.AcquiredPotionIds),
            EncounteredEnemyIds: Union(current.EncounteredEnemyIds, record.EncounteredEnemyIds));
    }

    private static ImmutableHashSet<string> Union(ImmutableHashSet<string> current, ImmutableArray<string> incoming)
    {
        if (incoming.IsDefaultOrEmpty) return current;
        var b = current.ToBuilder();
        foreach (var id in incoming)
            if (!string.IsNullOrEmpty(id)) b.Add(id);
        return b.ToImmutable();
    }
}
