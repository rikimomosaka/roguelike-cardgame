using RoguelikeCardGame.Core.Battle.Definitions;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Definitions;

public class EnemyPoolTests
{
    [Fact]
    public void Pool_holds_act_and_tier()
    {
        var p = new EnemyPool(2, EnemyTier.Elite);
        Assert.Equal(2, p.Act);
        Assert.Equal(EnemyTier.Elite, p.Tier);
    }

    [Fact]
    public void Two_pools_with_same_values_are_equal()
    {
        Assert.Equal(new EnemyPool(1, EnemyTier.Weak), new EnemyPool(1, EnemyTier.Weak));
    }
}
