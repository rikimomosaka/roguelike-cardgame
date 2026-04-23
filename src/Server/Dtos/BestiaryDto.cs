using System.Collections.Generic;

namespace RoguelikeCardGame.Server.Dtos;

public sealed record BestiaryDto(
    int SchemaVersion,
    IReadOnlyList<string> DiscoveredCardBaseIds,
    IReadOnlyList<string> DiscoveredRelicIds,
    IReadOnlyList<string> DiscoveredPotionIds,
    IReadOnlyList<string> EncounteredEnemyIds,
    IReadOnlyList<string> AllKnownCardBaseIds,
    IReadOnlyList<string> AllKnownRelicIds,
    IReadOnlyList<string> AllKnownPotionIds,
    IReadOnlyList<string> AllKnownEnemyIds);
