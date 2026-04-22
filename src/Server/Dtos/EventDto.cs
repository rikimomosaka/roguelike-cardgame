using System.Collections.Generic;

namespace RoguelikeCardGame.Server.Dtos;

public sealed record EventChoiceDto(
    string Label,
    string? ConditionSummary,
    IReadOnlyList<string> EffectSummaries);

public sealed record EventDto(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<EventChoiceDto> Choices);
