namespace RoguelikeCardGame.Server.Dtos;

/// <summary>
/// Optimistic-concurrency and idempotency metadata shared by battle commands
/// that do not otherwise require a request payload.
/// </summary>
public sealed record BattleCommandRequestDto(
    string? ParticipantId = null,
    string? ActorInstanceId = null,
    string? CommandId = null,
    long? ExpectedRevision = null);
