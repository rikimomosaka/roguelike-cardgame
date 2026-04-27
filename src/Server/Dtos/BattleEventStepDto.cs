namespace RoguelikeCardGame.Server.Dtos;

public sealed record BattleEventStepDto(
    BattleEventDto Event,
    BattleStateDto SnapshotAfter);
