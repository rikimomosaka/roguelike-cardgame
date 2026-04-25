using RoguelikeCardGame.Core.Battle.State;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.State;

public class BattlePhaseTests
{
    [Fact] public void PlayerInput_value_is_zero() => Assert.Equal(0, (int)BattlePhase.PlayerInput);
    [Fact] public void PlayerAttacking_value_is_one() => Assert.Equal(1, (int)BattlePhase.PlayerAttacking);
    [Fact] public void EnemyAttacking_value_is_two() => Assert.Equal(2, (int)BattlePhase.EnemyAttacking);
    [Fact] public void Resolved_value_is_three() => Assert.Equal(3, (int)BattlePhase.Resolved);
}
