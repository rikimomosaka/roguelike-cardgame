using RoguelikeCardGame.Core.Battle.Statuses;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Statuses;

public class StatusTickDirectionTests
{
    [Fact] public void None_value_is_zero()      => Assert.Equal(0, (int)StatusTickDirection.None);
    [Fact] public void Decrement_value_is_one()  => Assert.Equal(1, (int)StatusTickDirection.Decrement);
}
