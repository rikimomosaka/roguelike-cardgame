using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// Resolves actor-wide attack-scope overrides at the instant an attack is
/// executed or projected. Keeping this decision out of card queuing allows a
/// status granted by another participant later in the planning phase to affect
/// attacks that were already queued.
/// </summary>
internal static class AttackScopeRules
{
    public static EffectScope Resolve(CombatActor attacker, EffectScope original)
        => Resolve(
            original,
            attacker.GetStatus(BattleStatusIds.Omnistrike),
            attacker.GetStatus(BattleStatusIds.Disruption));

    public static EffectScope Resolve(
        EffectScope original,
        int omnistrike,
        int disruption)
    {
        if (original is not (EffectScope.Single or EffectScope.Random or EffectScope.All))
            return original;

        // Omnistrike is the stronger scope override and therefore wins when
        // both statuses are present.
        if (omnistrike > 0)
            return EffectScope.All;

        return disruption > 0
            ? EffectScope.Random
            : original;
    }
}
