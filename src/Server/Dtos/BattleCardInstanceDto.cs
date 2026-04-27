namespace RoguelikeCardGame.Server.Dtos;

public sealed record BattleCardInstanceDto(
    string InstanceId,
    string CardDefinitionId,
    bool IsUpgraded,
    int? CostOverride);
