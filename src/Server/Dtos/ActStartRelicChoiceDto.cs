using System.Collections.Generic;

namespace RoguelikeCardGame.Server.Dtos;

public sealed record ActStartRelicChoiceDto(IReadOnlyList<string> RelicIds);
