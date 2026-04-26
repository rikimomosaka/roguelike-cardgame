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

    [Fact] public void Display_no_dex_returns_sum()
    {
        var p = BlockPool.Empty.Add(5).Add(3);
        Assert.Equal(8, p.Display(dexterity: 0));
    }

    [Fact] public void Display_dex_adds_per_addcount()
    {
        // Sum=8, AddCount=2, dex=3 → 8 + 2*3 = 14
        var p = BlockPool.Empty.Add(5).Add(3);
        Assert.Equal(14, p.Display(dexterity: 3));
    }

    [Fact] public void Display_zero_when_empty()
    {
        Assert.Equal(0, BlockPool.Empty.Display(dexterity: 10));
    }

    [Fact] public void Consume_with_dex_uses_display()
    {
        // Sum=5, AddCount=2, dex=3 → Display=11、Consume(4) で残量 7
        var p = BlockPool.Empty.Add(2).Add(3);
        var after = p.Consume(incomingAttack: 4, dexterity: 3);
        Assert.Equal(7, after.Sum);
        Assert.Equal(0, after.AddCount);
    }

    [Fact] public void Consume_dex_overflow_clamps_to_zero()
    {
        // Display=8, attack=20 → 0
        // Sum=5, AddCount=1, dex=3 → Display=5+3=8、attack=20 → 0
        var p2 = BlockPool.Empty.Add(5);
        var after = p2.Consume(incomingAttack: 20, dexterity: 3);
        Assert.Equal(0, after.Sum);
        Assert.Equal(0, after.AddCount);
    }

    [Fact] public void Consume_with_zero_dex_matches_old_behavior()
    {
        // dex=0 で呼べば旧 Consume(int) と等価
        var p = BlockPool.Empty.Add(5).Add(5); // Sum=10, AddCount=2
        var after = p.Consume(incomingAttack: 3, dexterity: 0);
        Assert.Equal(7, after.Sum);
        Assert.Equal(0, after.AddCount);
    }
}
