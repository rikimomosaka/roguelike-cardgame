namespace RoguelikeCardGame.Server.Dtos;

public sealed record BattleEventDto(
    string Kind,
    int Order,
    string? CasterInstanceId,
    string? TargetInstanceId,
    int? Amount,
    string? CardId,
    string? Note);
