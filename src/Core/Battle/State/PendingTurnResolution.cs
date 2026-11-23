using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// Identifies the exact queued-turn boundary that remains after a human card
/// choice pauses resolution.  This is authoritative state, not replay UI
/// state: resuming from it must never execute an already completed actor twice.
/// </summary>
public enum TurnResolutionContinuationStage
{
    PlayerAttacking,
    EnemyAttacking,
    TurnStartContingencies,
}

/// <summary>
/// Cursor for a queued turn that yielded while an effect-only execution was
/// waiting for human input.  Actor id snapshots preserve the same membership
/// and ordering that the original phase resolver observed.
/// </summary>
public sealed record PendingTurnResolution(
    TurnResolutionContinuationStage Stage)
{
    /// <summary>
    /// Unprocessed ally actors for <see cref="TurnResolutionContinuationStage.PlayerAttacking"/>.
    /// The actor whose Afterimage requested the choice is already complete and
    /// is therefore deliberately absent.
    /// </summary>
    public ImmutableArray<string> RemainingAllyActorInstanceIds { get; init; } =
        ImmutableArray<string>.Empty;

    /// <summary>
    /// Unprocessed enemy actors for <see cref="TurnResolutionContinuationStage.EnemyAttacking"/>.
    /// A choice happens before an enemy action, so the current enemy remains at
    /// the head of this array.
    /// </summary>
    public ImmutableArray<string> RemainingEnemyActorInstanceIds { get; init; } =
        ImmutableArray<string>.Empty;

    /// <summary>
    /// The first remaining enemy already discarded its expiring block before
    /// a BeforeEnemyAttack Contingency requested input. This prevents resume
    /// from erasing block gained by the Contingency's counter-effects.
    /// </summary>
    public bool FirstEnemyBlockAlreadyReset { get; init; }

    /// <summary>
    /// Single-target actor selected when the first remaining enemy action
    /// started. A BeforeEnemyAttack Contingency may add or move allies before
    /// its human choice resumes, but that must not retarget the action. If the
    /// captured actor dies before the hit, the existing fixed-target rule
    /// intentionally lets the remaining Single hits miss.
    /// </summary>
    public string? FirstEnemySingleTargetActorInstanceId { get; init; }
}
