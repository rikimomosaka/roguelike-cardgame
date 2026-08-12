using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Events;

namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>A queued hit with stable attribution captured when it was added.</summary>
public sealed record AttackHit(
    int Amount,
    CombatSource Source,
    DamageCauseChain CauseChain)
{
    public static AttackHit Unattributed(int amount) =>
        new(amount, CombatSource.Unattributed, DamageCauseChain.Empty);
}
