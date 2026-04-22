using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Events;

public sealed record EventDefinition(
    string Id,
    string Name,
    string Description,
    ImmutableArray<EventChoice> Choices);
