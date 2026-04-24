using System.Collections.Generic;

namespace RoguelikeCardGame.Server.Dtos;

public sealed record EventChoiceDto(
    string Label,
    string? ConditionSummary,
    IReadOnlyList<string> EffectSummaries,
    string ResultMessage);

public sealed record EventDto(
    string Id,
    string Name,
    string StartMessage,
    IReadOnlyList<int> Tiers,
    string Rarity,
    string? ConditionSummary,
    IReadOnlyList<EventChoiceDto> Choices);
