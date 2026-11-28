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
    string? AssociatedSummonHeldInstanceId,
    IntentDto? Intent,
    IReadOnlyList<string>? StatusOrder = null,
    int BlockAddCount = 0,
    int? BlockRawSum = null,
    /// <summary>位相: "none" | "calm"（静）| "motion"（動）。</summary>
    string Phase = "none",
    /// <summary>基礎攻撃力（攻撃力参照カードの基準値。筋力は含まない）。</summary>
    int AttackPower = 0,
    /// <summary>基礎守備力（守備力参照カードの基準値。敏捷は含まない）。</summary>
    int DefensePower = 0);
