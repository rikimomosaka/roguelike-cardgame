using RoguelikeCardGame.Core.Battle.Definitions;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Definitions;

public class EnemyTierTests
{
    [Fact]
    public void Weak_value_is_zero() => Assert.Equal(0, (int)EnemyTier.Weak);

    [Fact]
    public void Strong_value_is_one() => Assert.Equal(1, (int)EnemyTier.Strong);

    [Fact]
    public void Elite_value_is_two() => Assert.Equal(2, (int)EnemyTier.Elite);

    [Fact]
    public void Boss_value_is_three() => Assert.Equal(3, (int)EnemyTier.Boss);
}
