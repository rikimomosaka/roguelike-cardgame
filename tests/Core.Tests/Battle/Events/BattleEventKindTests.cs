using RoguelikeCardGame.Core.Battle.Events;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Events;

public class BattleEventKindTests
{
    [Fact] public void BattleStart_value_is_zero() => Assert.Equal(0, (int)BattleEventKind.BattleStart);
    [Fact] public void TurnStart_value_is_one()   => Assert.Equal(1, (int)BattleEventKind.TurnStart);
    [Fact] public void PlayCard_value_is_two()    => Assert.Equal(2, (int)BattleEventKind.PlayCard);
    [Fact] public void AttackFire_value_is_three()=> Assert.Equal(3, (int)BattleEventKind.AttackFire);
    [Fact] public void DealDamage_value_is_four() => Assert.Equal(4, (int)BattleEventKind.DealDamage);
    [Fact] public void GainBlock_value_is_five()  => Assert.Equal(5, (int)BattleEventKind.GainBlock);
    [Fact] public void ActorDeath_value_is_six()  => Assert.Equal(6, (int)BattleEventKind.ActorDeath);
    [Fact] public void EndTurn_value_is_seven()   => Assert.Equal(7, (int)BattleEventKind.EndTurn);
    [Fact] public void BattleEnd_value_is_eight() => Assert.Equal(8, (int)BattleEventKind.BattleEnd);
}
