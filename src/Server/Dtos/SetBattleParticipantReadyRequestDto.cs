namespace RoguelikeCardGame.Server.Dtos;

public sealed record SetBattleParticipantReadyRequestDto(
    string ParticipantId,
    bool IsReady,
    long? ExpectedRevision = null,
    string? ActorInstanceId = null,
    string? CommandId = null);
