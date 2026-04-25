using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Cards;

public class EffectScopeTests
{
    [Fact]
    public void Self_value_is_zero() => Assert.Equal(0, (int)EffectScope.Self);

    [Fact]
    public void Single_value_is_one() => Assert.Equal(1, (int)EffectScope.Single);

    [Fact]
    public void Random_value_is_two() => Assert.Equal(2, (int)EffectScope.Random);

    [Fact]
    public void All_value_is_three() => Assert.Equal(3, (int)EffectScope.All);
}
