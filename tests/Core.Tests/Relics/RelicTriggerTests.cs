using RoguelikeCardGame.Core.Relics;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Relics;

public class RelicTriggerTests
{
    [Fact]
    public void OnPickup_value_is_zero() => Assert.Equal(0, (int)RelicTrigger.OnPickup);

    [Fact]
    public void Passive_value_is_one() => Assert.Equal(1, (int)RelicTrigger.Passive);

    [Fact]
    public void OnBattleStart_value_is_two() => Assert.Equal(2, (int)RelicTrigger.OnBattleStart);

    [Fact]
    public void OnBattleEnd_value_is_three() => Assert.Equal(3, (int)RelicTrigger.OnBattleEnd);

    [Fact]
    public void OnMapTileResolved_value_is_four() => Assert.Equal(4, (int)RelicTrigger.OnMapTileResolved);

    [Fact]
    public void OnTurnStart_value_is_five() => Assert.Equal(5, (int)RelicTrigger.OnTurnStart);

    [Fact]
    public void OnTurnEnd_value_is_six() => Assert.Equal(6, (int)RelicTrigger.OnTurnEnd);

    [Fact]
    public void OnCardPlay_value_is_seven() => Assert.Equal(7, (int)RelicTrigger.OnCardPlay);

    [Fact]
    public void OnEnemyDeath_value_is_eight() => Assert.Equal(8, (int)RelicTrigger.OnEnemyDeath);
}
