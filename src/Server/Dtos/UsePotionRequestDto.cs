namespace RoguelikeCardGame.Server.Dtos;

public sealed record UsePotionRequestDto(
    int PotionIndex,
    int? TargetEnemyIndex,
    int? TargetAllyIndex);
