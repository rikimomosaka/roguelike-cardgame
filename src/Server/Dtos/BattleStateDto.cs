using System.Collections.Generic;

namespace RoguelikeCardGame.Server.Dtos;

public sealed record BattleStateDto(
    string EncounterId,
    IReadOnlyList<EnemyInstanceDto> Enemies,
    string Outcome);

public sealed record EnemyInstanceDto(
    string EnemyDefinitionId,
    string Name,
    string ImageId,
    int CurrentHp,
    int MaxHp,
    string CurrentMoveId);
