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
    [Fact] public void ApplyStatus_value_is_nine()    => Assert.Equal(9,  (int)BattleEventKind.ApplyStatus);
    [Fact] public void RemoveStatus_value_is_ten()    => Assert.Equal(10, (int)BattleEventKind.RemoveStatus);
    [Fact] public void PoisonTick_value_is_eleven()   => Assert.Equal(11, (int)BattleEventKind.PoisonTick);
    [Fact] public void Heal_value_is_12()         => Assert.Equal(12, (int)BattleEventKind.Heal);
    [Fact] public void Draw_value_is_13()         => Assert.Equal(13, (int)BattleEventKind.Draw);
    [Fact] public void Discard_value_is_14()      => Assert.Equal(14, (int)BattleEventKind.Discard);
    [Fact] public void Upgrade_value_is_15()      => Assert.Equal(15, (int)BattleEventKind.Upgrade);
    [Fact] public void Exhaust_value_is_16()      => Assert.Equal(16, (int)BattleEventKind.Exhaust);
    [Fact] public void GainEnergy_value_is_17()   => Assert.Equal(17, (int)BattleEventKind.GainEnergy);
    [Fact] public void Summon_value_is_18()       => Assert.Equal(18, (int)BattleEventKind.Summon);
}
