using System;

namespace RoguelikeCardGame.Core.Run;

public static class DebugActions
{
    public static RunState ApplyDamage(RunState state, int amount)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        int next = Math.Max(0, state.CurrentHp - amount);
        return state with { CurrentHp = next };
    }
}
