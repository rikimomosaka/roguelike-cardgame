using RoguelikeCardGame.Core.Battle.Definitions;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Definitions;

public class MoveKindTests
{
    [Fact]
    public void Attack_value_is_zero() => Assert.Equal(0, (int)MoveKind.Attack);

    [Fact]
    public void Defend_value_is_one() => Assert.Equal(1, (int)MoveKind.Defend);

    [Fact]
    public void Buff_value_is_two() => Assert.Equal(2, (int)MoveKind.Buff);

    [Fact]
    public void Debuff_value_is_three() => Assert.Equal(3, (int)MoveKind.Debuff);

    [Fact]
    public void Heal_value_is_four() => Assert.Equal(4, (int)MoveKind.Heal);

    [Fact]
    public void Multi_value_is_five() => Assert.Equal(5, (int)MoveKind.Multi);

    [Fact]
    public void Unknown_value_is_six() => Assert.Equal(6, (int)MoveKind.Unknown);
}
