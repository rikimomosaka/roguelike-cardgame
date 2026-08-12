using System.Collections.Generic;

namespace RoguelikeCardGame.Server.Dtos;

public sealed record BattleStateDto(
    int Turn,
    string Phase,
    string Outcome,
    IReadOnlyList<CombatActorDto> Allies,
    IReadOnlyList<CombatActorDto> Enemies,
    int? TargetAllyIndex,
    int? TargetEnemyIndex,
    int Energy,
    int EnergyPerTurn, // ターン開始時の追加量。保有上限ではない
    IReadOnlyList<BattleCardInstanceDto> DrawPile,
    IReadOnlyList<BattleCardInstanceDto> Hand,
    IReadOnlyList<BattleCardInstanceDto> DiscardPile,
    IReadOnlyList<BattleCardInstanceDto> ExhaustPile,
    IReadOnlyList<BattleCardInstanceDto> SummonHeld,
    IReadOnlyList<BattleCardInstanceDto> PowerCards,
    int ComboCount,
    int MaxComboThisTurn,
    int ComboGauge,
    IReadOnlyList<string> UsedCharacterSkillIdsThisTurn,
    int? LastPlayedOrigCost,
    bool NextCardComboFreePass,
    IReadOnlyList<string> OwnedRelicIds,
    IReadOnlyList<string> Potions,
    string EncounterId,
    PendingCardPlayDto? PendingCardPlay = null);
