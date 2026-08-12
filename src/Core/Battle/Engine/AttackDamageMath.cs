namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// Shared per-hit attacker-side damage adjustment.
/// Keeping preview and resolution on the same rule prevents intent drift.
/// </summary>
internal static class AttackDamageMath
{
    public static int AdjustForAttacker(int baseAmount, int strength, int weak)
    {
        long boosted = (long)baseAmount + strength;
        int adjusted = weak > 0
            ? (int)(boosted * 3 / 4)
            : (int)boosted;
        return System.Math.Max(0, adjusted);
    }
}
