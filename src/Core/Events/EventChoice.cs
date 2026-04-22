using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Events;

public sealed record EventChoice(
    string Label,
    EventCondition? Condition,
    ImmutableArray<EventEffect> Effects);
