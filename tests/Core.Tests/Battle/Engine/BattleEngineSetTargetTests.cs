using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class BattleEngineSetTargetTests
{
    private static BattleState Make(
        int? targetAlly = 0,
        int? targetEnemy = 0,
        int allyCount = 1,
        int enemyCount = 2,
        BattlePhase phase = BattlePhase.PlayerInput,
        bool enemy0Dead = false)
    {
        var allies = new System.Collections.Generic.List<CombatActor>();
        for (int i = 0; i < allyCount; i++)
            allies.Add(BattleFixtures.Hero(slotIndex: i));
        var enemies = new System.Collections.Generic.List<CombatActor>();
        for (int i = 0; i < enemyCount; i++)
        {
            var e = BattleFixtures.Goblin(slotIndex: i);
            if (i == 0 && enemy0Dead) e = e with { CurrentHp = 0 };
            enemies.Add(e);
        }
        return new BattleState(
            Turn: 1, Phase: phase, Outcome: BattleOutcome.Pending,
            Allies: allies.ToImmutableArray(),
            Enemies: enemies.ToImmutableArray(),
            TargetAllyIndex: targetAlly, TargetEnemyIndex: targetEnemy,
            Energy: 3, EnergyMax: 3,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0, LastPlayedOrigCost: null, NextCardComboFreePass: false,
            OwnedRelicIds: ImmutableArray<string>.Empty,
            Potions: ImmutableArray<string>.Empty,
            EncounterId: "enc_test");
    }

    [Fact] public void Switches_enemy_target_to_alive_slot()
    {
        var s = Make(enemyCount: 3);
        var next = BattleEngine.SetTarget(s, ActorSide.Enemy, 2);
        Assert.Equal(2, next.TargetEnemyIndex);
        Assert.Equal(0, next.TargetAllyIndex); // 味方は変えない
    }

    [Fact] public void Switches_ally_target_to_alive_slot()
    {
        var s = Make(allyCount: 2);
        var next = BattleEngine.SetTarget(s, ActorSide.Ally, 1);
        Assert.Equal(1, next.TargetAllyIndex);
        Assert.Equal(0, next.TargetEnemyIndex); // 敵は変えない
    }

    [Fact] public void Returns_BattleState_only_no_events()
    {
        var s = Make();
        BattleState next = BattleEngine.SetTarget(s, ActorSide.Enemy, 0);
        Assert.Equal(0, next.TargetEnemyIndex);
    }

    [Theory]
    [InlineData(BattlePhase.PlayerAttacking)]
    [InlineData(BattlePhase.EnemyAttacking)]
    [InlineData(BattlePhase.Resolved)]
    public void Throws_when_phase_not_PlayerInput(BattlePhase phase)
    {
        var s = Make(phase: phase);
        var ex = Assert.Throws<System.InvalidOperationException>(() =>
            BattleEngine.SetTarget(s, ActorSide.Enemy, 0));
        Assert.Contains("Phase=PlayerInput", ex.Message);
    }

    [Fact] public void Throws_when_slotIndex_negative()
    {
        var s = Make();
        var ex = Assert.Throws<System.InvalidOperationException>(() =>
            BattleEngine.SetTarget(s, ActorSide.Enemy, -1));
        Assert.Contains("out of range", ex.Message);
    }

    [Fact] public void Throws_when_slotIndex_too_large()
    {
        var s = Make(enemyCount: 2);
        Assert.Throws<System.InvalidOperationException>(() =>
            BattleEngine.SetTarget(s, ActorSide.Enemy, 2));
        Assert.Throws<System.InvalidOperationException>(() =>
            BattleEngine.SetTarget(s, ActorSide.Enemy, 99));
    }

    [Fact] public void Throws_when_target_slot_is_dead()
    {
        var s = Make(enemyCount: 2, enemy0Dead: true);
        var ex = Assert.Throws<System.InvalidOperationException>(() =>
            BattleEngine.SetTarget(s, ActorSide.Enemy, 0));
        Assert.Contains("dead", ex.Message);
    }

    [Fact] public void Allows_switching_to_alive_when_other_slot_dead()
    {
        var s = Make(enemyCount: 2, enemy0Dead: true);
        var next = BattleEngine.SetTarget(s, ActorSide.Enemy, 1);
        Assert.Equal(1, next.TargetEnemyIndex);
    }
}
