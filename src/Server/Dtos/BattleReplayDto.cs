namespace RoguelikeCardGame.Server.Dtos;

/// <summary>A filtered, append-only replay feed. Sequence values remain battle-global.</summary>
public sealed record BattleReplayDto(
    long FromSequenceExclusive,
    long LatestSequence,
    long ReturnedThroughSequence,
    int? Turn,
    string? ParticipantId,
    string? ActorInstanceId,
    string FeedState,
    bool IsReady,
    bool IsComplete,
    IReadOnlyList<BattleReplayParticipantFeedDto> ParticipantFeeds,
    IReadOnlyList<BattleReplayEntryDto> Entries);

public sealed record BattleReplayParticipantFeedDto(
    int Turn,
    string ParticipantId,
    string State,
    bool IsReady,
    bool IsComplete,
    long LastSequence);

public sealed record BattleReplayEntryDto(
    long Sequence,
    string EntryId,
    int Turn,
    string CommandId,
    string Kind,
    string ViewKind,
    string? ParticipantId,
    string? ActorInstanceId,
    long RevisionBefore,
    long RevisionAfter,
    BattleReplayCardDto? Card,
    IReadOnlyList<string> TargetActorInstanceIds,
    IReadOnlyList<BattleReplayEffectDto> Effects,
    IReadOnlyList<BattleReplayIntentDto> Intents,
    IReadOnlyList<BattleReplayFeedChangeDto> FeedChanges,
    bool IsResolution,
    string? Note,
    IReadOnlyList<string> SelectedInstanceIds,
    BattleReplayAbilityDto? Ability,
    BattleStateDto? StateBefore,
    BattleStateDto? StateAfter,
    BattleReplaySourceDto? Source,
    int? ComboCountAfter = null);

/// <summary>
/// Human-readable action origin retained in compact inbound-effect feeds where
/// the source's full card or ability details are intentionally hidden.
/// </summary>
public sealed record BattleReplaySourceDto(
    string? ActorInstanceId,
    string ActorDisplayName,
    string ActionKind,
    string? ActionDefinitionId,
    string ActionDisplayName);

public sealed record BattleReplayCardDto(
    string CardInstanceId,
    string DefinitionId,
    string DisplayName,
    string CardType,
    string Rarity,
    bool IsUpgraded,
    int OriginalCost,
    int CurrentCost,
    int PaidCost,
    string Description,
    IReadOnlyList<BattleReplayCardCostChangeDto> CostChanges,
    string? PoolClass = null);

public sealed record BattleReplayCardCostChangeDto(
    string Kind,
    long Value,
    string? SourceId,
    string? SourceName);

public sealed record BattleReplayAbilityDto(
    string DefinitionId,
    string DisplayName,
    string Kind,
    string Description,
    int EnergyCost,
    int ComboGaugeCost,
    int UsesBefore,
    int UsesPerTurn);

public sealed record BattleReplayEffectDto(
    string Kind,
    string? SourceActorInstanceId,
    string? TargetParticipantId,
    string? TargetActorInstanceId,
    int? Amount,
    string? StatusId,
    string? Description,
    int? ActualHpLoss,
    int? BlockedAmount,
    int? HitIndex,
    bool? HitLanded,
    string? CardDefinitionId = null,
    string? CardInstanceId = null,
    BattleReplayCardDto? Card = null);

public sealed record BattleReplayIntentDto(
    string Kind,
    string Scope,
    int Amount,
    int Hits,
    string? TargetParticipantId,
    string? TargetActorInstanceId);

public sealed record BattleReplayFeedChangeDto(
    string ParticipantId,
    bool? IsReady,
    bool? IsComplete);
