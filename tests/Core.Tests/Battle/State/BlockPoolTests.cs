using RoguelikeCardGame.Core.Battle.State;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.State;

public class BlockPoolTests
{
    [Fact] public void Empty_is_zero()
    {
        var p = BlockPool.Empty;
        Assert.Equal(0, p.Sum);
        Assert.Equal(0, p.AddCount);
    }

    [Fact] public void Add_increments_sum_and_addcount()
    {
        var p = BlockPool.Empty.Add(5).Add(3);
        Assert.Equal(8, p.Sum);
        Assert.Equal(2, p.AddCount);
    }

    [Fact] public void Consume_partial_keeps_remainder_resets_addcount()
    {
        var p = BlockPool.Empty.Add(5).Add(5); // Sum=10, AddCount=2
        var after = p.Consume(3);              // 10 - 3 = 7
        Assert.Equal(7, after.Sum);
        Assert.Equal(0, after.AddCount);
    }

    [Fact] public void Consume_overflow_clamps_to_zero()
    {
        var p = BlockPool.Empty.Add(5);
        var after = p.Consume(20);
        Assert.Equal(0, after.Sum);
        Assert.Equal(0, after.AddCount);
    }

    [Fact] public void Consume_zero_keeps_sum_but_resets_addcount()
    {
        var p = BlockPool.Empty.Add(5).Add(2);
        var after = p.Consume(0);
        Assert.Equal(7, after.Sum);
        Assert.Equal(0, after.AddCount);
    }
}
