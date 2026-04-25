using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Cards;

public class EffectSideTests
{
    [Fact]
    public void Enemy_value_is_zero() => Assert.Equal(0, (int)EffectSide.Enemy);

    [Fact]
    public void Ally_value_is_one() => Assert.Equal(1, (int)EffectSide.Ally);
}
