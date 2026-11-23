namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// Shared per-hit attacker-side damage adjustment.
/// Keeping preview and resolution on the same rule prevents intent drift.
/// </summary>
internal static class AttackDamageMath
{
    public static int AdjustForAttacker(
        int baseAmount,
        int strength,
        int weak,
        int vigor = 0)
    {
        long boosted = (long)baseAmount + strength;
        long adjusted = weak > 0 ? boosted * 3 / 4 : boosted;
        if (vigor > 0)
            adjusted = adjusted * 3 / 2;
        return (int)System.Math.Clamp(adjusted, 0L, int.MaxValue);
    }
}
