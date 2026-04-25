using RoguelikeCardGame.Core.Battle.State;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.State;

public class BattleCardInstanceTests
{
    [Fact] public void Record_equality_holds()
    {
        var a = new BattleCardInstance("inst1", "strike", false, null);
        var b = new BattleCardInstance("inst1", "strike", false, null);
        Assert.Equal(a, b);
    }

    [Fact] public void CostOverride_can_be_null_or_value()
    {
        var noOverride = new BattleCardInstance("inst1", "strike", false, null);
        var withOverride = new BattleCardInstance("inst1", "strike", false, 0);
        Assert.Null(noOverride.CostOverride);
        Assert.Equal(0, withOverride.CostOverride);
    }

    [Fact] public void IsUpgraded_flag_distinguishes_records()
    {
        var plain = new BattleCardInstance("inst1", "strike", false, null);
        var upgraded = new BattleCardInstance("inst1", "strike", true, null);
        Assert.NotEqual(plain, upgraded);
    }
}
