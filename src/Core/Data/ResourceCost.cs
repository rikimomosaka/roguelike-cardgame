namespace RoguelikeCardGame.Core.Data;

/// <summary>
/// A cost paid by an action.  Keeping the resource dimensions together lets
/// cards and character skills share the same cost model.
/// </summary>
public sealed record ResourceCost(int Energy = 0, int ComboGauge = 0)
{
    public bool IsValid => Energy >= 0 && ComboGauge >= 0;
}
