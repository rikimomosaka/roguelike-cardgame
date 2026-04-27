using System.Collections.Generic;

namespace RoguelikeCardGame.Server.Dtos;

public sealed record BattleActionResponseDto(
    BattleStateDto State,
    IReadOnlyList<BattleEventStepDto> Steps);
