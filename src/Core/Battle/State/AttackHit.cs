using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Events;

namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>A queued hit with stable attribution captured when it was added.</summary>
public sealed record AttackHit(
    int Amount,
    CombatSource Source,
    DamageCauseChain CauseChain)
{
    /// <summary>
    /// The enemy selected when this single-target hit was queued. Null keeps
    /// legacy saves and manually constructed pools compatible; the resolver
    /// snapshots the actor's selected enemy when that legacy chain begins.
    /// Random and all-target pools intentionally leave this unset.
    /// </summary>
    public string? TargetInstanceId { get; init; }

    public static AttackHit Unattributed(int amount) =>
        new(amount, CombatSource.Unattributed, DamageCauseChain.Empty);
}
