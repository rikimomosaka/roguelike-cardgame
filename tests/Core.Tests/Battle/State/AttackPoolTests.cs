using RoguelikeCardGame.Core.Battle.State;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.State;

public class AttackPoolTests
{
    [Fact] public void Empty_has_zero_sum_and_count()
    {
        var p = AttackPool.Empty;
        Assert.Equal(0, p.Sum);
        Assert.Equal(0, p.AddCount);
        Assert.Equal(0, p.RawTotal);
    }

    [Fact] public void Add_increments_sum_and_addcount()
    {
        var p = AttackPool.Empty.Add(5).Add(3);
        Assert.Equal(8, p.Sum);
        Assert.Equal(2, p.AddCount);
        Assert.Equal(8, p.RawTotal);
    }

    [Fact] public void RawTotal_equals_Sum()
    {
        var p = AttackPool.Empty.Add(7).Add(0).Add(11);
        Assert.Equal(p.Sum, p.RawTotal);
    }

    [Fact] public void Add_zero_still_increments_addcount()
    {
        // 力バフ遡及計算 (10.2.B) で AddCount × strength が乗るため、amount=0 でも AddCount は +1
        var p = AttackPool.Empty.Add(0);
        Assert.Equal(0, p.Sum);
        Assert.Equal(1, p.AddCount);
    }
}
