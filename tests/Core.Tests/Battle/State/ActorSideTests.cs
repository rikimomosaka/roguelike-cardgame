using RoguelikeCardGame.Core.Battle.State;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.State;

public class ActorSideTests
{
    [Fact] public void Ally_value_is_zero() => Assert.Equal(0, (int)ActorSide.Ally);
    [Fact] public void Enemy_value_is_one() => Assert.Equal(1, (int)ActorSide.Enemy);
}
