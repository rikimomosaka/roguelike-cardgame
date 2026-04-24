using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Events;

public sealed record EventDefinition(
    string Id,
    string Name,
    string StartMessage,
    ImmutableArray<EventChoice> Choices,
    ImmutableArray<int> Tiers,
    EventRarity Rarity = EventRarity.Common,
    EventCondition? Condition = null);
