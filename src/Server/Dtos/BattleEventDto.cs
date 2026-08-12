namespace RoguelikeCardGame.Server.Dtos;

public sealed record BattleEventDto(
    string Kind,
    int Order,
    string? CasterInstanceId,
    string? TargetInstanceId,
    int? Amount,
    string? CardId,
    string? Note,
    int? BlockedAmount,
    int? HitIndex,
    string? DamageType,
    CombatSourceDto? Source,
    IReadOnlyList<DamageCauseFrameDto> CauseChain,
    int? ActualHpLoss,
    int? Overkill,
    bool? HitLanded);

public sealed record CombatSourceDto(
    string Kind,
    int? OwnerSlot,
    string? OwnerInstanceId,
    string? ActorInstanceId,
    string? DefinitionId,
    string? InstanceId,
    string? MoveId,
    int? EffectIndex,
    string? Scope);

public sealed record DamageCauseFrameDto(
    int Depth,
    string DamageType,
    CombatSourceDto Source,
    int? HitIndex,
    string Cause);
