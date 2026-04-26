using RoguelikeCardGame.Core.Battle.Statuses;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Statuses;

public class StatusKindTests
{
    [Fact] public void Buff_value_is_zero()   => Assert.Equal(0, (int)StatusKind.Buff);
    [Fact] public void Debuff_value_is_one()  => Assert.Equal(1, (int)StatusKind.Debuff);
}
