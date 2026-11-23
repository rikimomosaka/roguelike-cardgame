namespace RoguelikeCardGame.Server.Dtos;

/// <summary>
/// One independently controlled ally's card resources. Shared combat actors
/// remain on <see cref="BattleStateDto.Allies"/>; this projection owns the
/// participant-specific zones, combo state, targets and Ready state.
/// </summary>
public sealed record BattleParticipantDto(
    string ParticipantId,
    string ActorInstanceId,
    string ControllerKind,
    int? OwnerPlayerSlot,
    string CharacterId,
    int? TargetAllyIndex,
    int? TargetEnemyIndex,
    int Energy,
    int EnergyPerTurn,
    int DrawPerTurn,
    IReadOnlyList<BattleCardInstanceDto> DrawPile,
    IReadOnlyList<BattleCardInstanceDto> Hand,
    IReadOnlyList<BattleCardInstanceDto> DiscardPile,
    IReadOnlyList<BattleCardInstanceDto> ExhaustPile,
    IReadOnlyList<BattleCardInstanceDto> SummonHeld,
    IReadOnlyList<BattleCardInstanceDto> PowerCards,
    int ComboCount,
    int MaxComboThisTurn,
    int ComboGauge,
    int? LastPlayedOrigCost,
    bool NextCardComboFreePass,
    bool WildUsedInCurrentCombo,
    IReadOnlyList<string> UsedCharacterSkillIdsThisTurn,
    IReadOnlyDictionary<string, int> CharacterSkillUsesThisTurn,
    IReadOnlyDictionary<string, int> CardUsesThisTurn,
    bool CardUseLockedThisTurn,
    IReadOnlyList<string> OwnedRelicIds,
    IReadOnlyList<string> Potions,
    IReadOnlyDictionary<string, int> PotionUsesThisTurn,
    PendingCardPlayDto? PendingCardPlay,
    bool CharacterSkillsRestricted,
    int CpuCardPlaysThisTurn,
    bool IsReady,
    FinisherRecordDto? FinisherRecord = null,
    bool ComboCarryoverPending = false);
