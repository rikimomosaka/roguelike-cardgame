using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// Phase 10.5.M2-Choose: PlayCard 中で choose effect に到達した時の pause 状態。
/// Resolve されるまで PlayCard は呼べない (validate で reject)。
/// BattleState は in-memory のみ (Server BattleSession.State) なので save schema 影響なし。
/// </summary>
public sealed record PendingCardPlay(
    string CardInstanceId,
    int EffectIndex,
    ImmutableArray<string> SummonedActorInstanceIdsBefore,
    PendingChoice Choice)
{
    /// <summary>
    /// Non-null when the choice belongs to an effect-only execution rather
    /// than the normal effect list of <see cref="CardInstanceId"/>.
    /// </summary>
    public PendingEffectExecution? EffectExecution { get; init; }
}

public enum EffectExecutionKind
{
    Reprise,
    Contingency,
    Afterimage,
}

/// <summary>
/// Continuation metadata for a copied effect list.  The source snapshot is
/// independent from the real card whose lifecycle may still need finalizing.
/// </summary>
public sealed record PendingEffectExecution(
    EffectExecutionKind Kind,
    PlayedCardEffectSnapshot SourceCard,
    string CasterActorInstanceId,
    string CauseDefinitionId)
{
    public ImmutableArray<PlayedCardEffectSnapshot> RemainingCards { get; init; } =
        ImmutableArray<PlayedCardEffectSnapshot>.Empty;

    public int RemainingCycles { get; init; }

    public string? ArmedContingencyCardInstanceId { get; init; }

    public int BeforeSingleAttackCount { get; init; }

    public int BeforeRandomAttackCount { get; init; }

    public int BeforeAllAttackCount { get; init; }

    public int? TriggerPreviousCombo { get; init; }

    public int? TriggerCurrentCombo { get; init; }

    /// <summary>
    /// A Finisher card whose final combo snapshot/reset must run after every
    /// ComboReached Contingency (including human choices) has completed.
    /// </summary>
    public BattleCardInstance? PostContingencyFinisherCard { get; init; }

    public string? PostContingencyFinisherActorInstanceId { get; init; }
}

/// <summary>
/// Phase 10.5.M2-Choose: choose effect の選択候補と要件。
/// </summary>
public sealed record PendingChoice(
    string Action,
    string Pile,
    int Count,
    ImmutableArray<string> CandidateInstanceIds);
