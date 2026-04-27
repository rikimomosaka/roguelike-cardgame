using System.Collections.Generic;

namespace RoguelikeCardGame.Server.Dtos;

public sealed record CombatActorDto(
    string InstanceId,
    string DefinitionId,
    string Side,
    int SlotIndex,
    int CurrentHp,
    int MaxHp,
    int BlockDisplay,
    int AttackSingleDisplay,
    int AttackRandomDisplay,
    int AttackAllDisplay,
    IReadOnlyDictionary<string, int> Statuses,
    string? CurrentMoveId,
    int? RemainingLifetimeTurns,
    string? AssociatedSummonHeldInstanceId);
