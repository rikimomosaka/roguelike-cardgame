namespace RoguelikeCardGame.Server.Dtos;

public sealed record PlayCardRequestDto(
    int HandIndex,
    int? TargetEnemyIndex,
    int? TargetAllyIndex,
    string? ParticipantId = null,
    string? ActorInstanceId = null,
    string? CardInstanceId = null,
    string? CommandId = null,
    long? ExpectedRevision = null);
