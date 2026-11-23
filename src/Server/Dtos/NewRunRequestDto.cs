using System.Collections.Generic;

namespace RoguelikeCardGame.Server.Dtos;

/// <summary>
/// Optional character picks for a new run. A missing body or null list keeps the
/// legacy default-character behaviour; an explicitly supplied list is validated.
/// </summary>
public sealed record NewRunRequestDto(IReadOnlyList<string>? CharacterIds);
