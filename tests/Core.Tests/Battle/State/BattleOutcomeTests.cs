using RoguelikeCardGame.Core.Battle.State;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.State;

public class BattleOutcomeTests
{
    [Fact] public void Pending_value_is_zero() => Assert.Equal(0, (int)BattleOutcome.Pending);
    [Fact] public void Victory_value_is_one() => Assert.Equal(1, (int)BattleOutcome.Victory);
    [Fact] public void Defeat_value_is_two() => Assert.Equal(2, (int)BattleOutcome.Defeat);
}
