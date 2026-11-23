namespace RoguelikeCardGame.Server.Dtos;

public sealed record UsePotionRequestDto(
    int PotionIndex,
    int? TargetEnemyIndex,
    int? TargetAllyIndex,
    string? ParticipantId = null,
    string? ActorInstanceId = null,
    string? PotionId = null,
    string? CommandId = null,
    long? ExpectedRevision = null);
