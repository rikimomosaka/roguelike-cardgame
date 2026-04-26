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

    [Fact] public void Display_no_status_returns_sum()
    {
        var p = AttackPool.Empty.Add(5).Add(3);
        Assert.Equal(8, p.Display(strength: 0, weak: 0));
    }

    [Fact] public void Display_strength_adds_per_addcount()
    {
        // Sum=8, AddCount=2, strength=3 → 8 + 2*3 = 14
        var p = AttackPool.Empty.Add(5).Add(3);
        Assert.Equal(14, p.Display(strength: 3, weak: 0));
    }

    [Fact] public void Display_weak_applies_three_quarters_floor()
    {
        // Sum=10, AddCount=1, strength=0, weak=1 → floor(10 * 0.75) = 7
        var p = AttackPool.Empty.Add(10);
        Assert.Equal(7, p.Display(strength: 0, weak: 1));
    }

    [Fact] public void Display_weak_with_strength()
    {
        // Sum=8, AddCount=2, strength=3 → 8+6 = 14、weak: floor(14 * 0.75) = 10
        var p = AttackPool.Empty.Add(5).Add(3);
        Assert.Equal(10, p.Display(strength: 3, weak: 1));
    }

    [Fact] public void Display_zero_when_empty()
    {
        Assert.Equal(0, AttackPool.Empty.Display(strength: 5, weak: 0));
    }

    [Fact] public void Operator_plus_sums_both_fields()
    {
        var a = AttackPool.Empty.Add(5).Add(3);   // Sum=8, AddCount=2
        var b = AttackPool.Empty.Add(2);          // Sum=2, AddCount=1
        var c = a + b;                            // Sum=10, AddCount=3
        Assert.Equal(10, c.Sum);
        Assert.Equal(3, c.AddCount);
    }

    [Fact] public void Operator_plus_with_empty_returns_other()
    {
        var a = AttackPool.Empty.Add(5);
        var c = a + AttackPool.Empty;
        Assert.Equal(a, c);
    }
}
