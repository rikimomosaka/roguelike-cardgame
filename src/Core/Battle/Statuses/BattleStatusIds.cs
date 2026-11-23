namespace RoguelikeCardGame.Core.Battle.Statuses;

/// <summary>
/// Canonical battle-status identifiers shared by rules, projections and authoring.
/// Keeping identifiers here prevents string drift between otherwise independent
/// battle subsystems.
/// </summary>
public static class BattleStatusIds
{
    public const string Strength = "strength";
    public const string Dexterity = "dexterity";
    public const string Omnistrike = "omnistrike";
    public const string Barrier = "barrier";
    public const string Lifesteal = "lifesteal";
    public const string Reflect = "reflect";
    public const string HandCostReduction = "handCostReduction";
    public const string Weak = "weak";
    public const string Vulnerable = "vulnerable";
    public const string Poison = "poison";
    public const string HandCostIncrease = "handCostIncrease";

    public const string Vigor = "vigor";
    public const string Pursuit = "pursuit";
    public const string Regeneration = "regeneration";
    public const string Fortify = "fortify";
    public const string Resistance = "resistance";
    public const string Revive = "revive";
    public const string Growth = "growth";
    public const string Insight = "insight";
    public const string Haste = "haste";
    public const string Continuity = "continuity";
    public const string Mastery = "mastery";

    public const string Afterimage = "afterimage";
    public const string Steadfast = "steadfast";
    public const string Cover = "cover";
    public const string DefensiveStance = "defensiveStance";
    public const string ComboCarryover = "comboCarryover";
    public const string Afterglow = "afterglow";
    public const string Resonance = "resonance";

    public const string VirulentPoison = "virulentPoison";
    public const string Stagnation = "stagnation";
    public const string Burn = "burn";
    public const string Scorch = "scorch";
    public const string Confusion = "confusion";
    public const string Curse = "curse";
    public const string Bind = "bind";
    public const string Silence = "silence";
    public const string Seal = "seal";
    public const string Exhaustion = "exhaustion";
    public const string Distress = "distress";
    public const string Disruption = "disruption";
}
