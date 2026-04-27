using System.Collections.Generic;

namespace RoguelikeCardGame.Server.Dtos;

public sealed record BattlePlaceholderStateDto(
    string EncounterId,
    IReadOnlyList<PlaceholderEnemyInstanceDto> Enemies,
    string Outcome);

public sealed record PlaceholderEnemyInstanceDto(
    string EnemyDefinitionId,
    string Name,
    string ImageId,
    int CurrentHp,
    int MaxHp,
    string CurrentMoveId);
