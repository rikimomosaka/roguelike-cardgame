namespace RoguelikeCardGame.Server.Dtos;

public sealed record SetTargetRequestDto(
    string Side,
    int SlotIndex,
    string? ParticipantId = null,
    string? ActorInstanceId = null,
    string? CommandId = null,
    long? ExpectedRevision = null);
