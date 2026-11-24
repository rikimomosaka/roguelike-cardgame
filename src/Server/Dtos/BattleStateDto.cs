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
    IReadOnlyDictionary<string, int> CharacterSkillUsesThisTurn,
    int? LastPlayedOrigCost,
    bool NextCardComboFreePass,
    IReadOnlyList<string> OwnedRelicIds,
    IReadOnlyList<string> Potions,
    IReadOnlyDictionary<string, int> PotionUsesThisTurn,
    string EncounterId,
    PendingCardPlayDto? PendingCardPlay = null,
    bool CharacterSkillsRestricted = false,
    long ActionRevision = 0,
    IReadOnlyList<BattleParticipantDto>? Participants = null,
    FinisherRecordDto? FinisherRecord = null,
    bool ComboCarryoverPending = false,
    IReadOnlyList<EnemyParticipantSummaryDto>? EnemyParticipants = null,
    /// <summary>主参加者がこのターンに使用したカード（使用順）。プレイ履歴表示用。</summary>
    IReadOnlyList<FinisherRecordCardDto>? PlayHistoryThisTurn = null);

/// <summary>
/// カード制御の敵のカード資源の概要（行動再生の入口・開発確認用）。
/// 手札の中身は公開しない（枚数のみ）。
/// </summary>
public sealed record EnemyParticipantSummaryDto(
    string ParticipantId,
    string ActorInstanceId,
    int Energy,
    int HandCount,
    int DrawPileCount,
    int DiscardPileCount,
    int ExhaustPileCount);
