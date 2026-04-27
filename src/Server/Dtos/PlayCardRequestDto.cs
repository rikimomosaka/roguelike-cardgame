namespace RoguelikeCardGame.Server.Dtos;

public sealed record PlayCardRequestDto(
    int HandIndex,
    int? TargetEnemyIndex,
    int? TargetAllyIndex);
