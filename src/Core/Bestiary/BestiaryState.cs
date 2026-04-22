using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Bestiary;

/// <summary>アカウント単位で発見済み ID を蓄積する図鑑ステート。</summary>
public sealed record BestiaryState(
    int SchemaVersion,
    ImmutableHashSet<string> DiscoveredCardBaseIds,
    ImmutableHashSet<string> DiscoveredRelicIds,
    ImmutableHashSet<string> DiscoveredPotionIds,
    ImmutableHashSet<string> EncounteredEnemyIds)
{
    public const int CurrentSchemaVersion = 1;

    public static BestiaryState Empty { get; } = new(
        CurrentSchemaVersion,
        ImmutableHashSet<string>.Empty,
        ImmutableHashSet<string>.Empty,
        ImmutableHashSet<string>.Empty,
        ImmutableHashSet<string>.Empty);
}
