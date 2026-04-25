using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class TargetingAutoSwitchTests
{
    private static BattleState Make(int? tgtE, params CombatActor[] enemies) => new(
        Turn: 1, Phase: BattlePhase.PlayerAttacking, Outcome: BattleOutcome.Pending,
        Allies: ImmutableArray.Create(BattleFixtures.Hero()),
        Enemies: enemies.ToImmutableArray(),
        TargetAllyIndex: 0, TargetEnemyIndex: tgtE,
        Energy: 0, EnergyMax: 3,
        DrawPile: ImmutableArray<BattleCardInstance>.Empty,
        Hand: ImmutableArray<BattleCardInstance>.Empty,
        DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
        ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
        EncounterId: "enc_test");

    [Fact] public void Dead_target_switches_to_innermost_alive()
    {
        var dead = BattleFixtures.Goblin(slotIndex: 0) with { CurrentHp = 0 };
        var alive1 = BattleFixtures.Goblin(slotIndex: 1, hp: 10);
        var alive2 = BattleFixtures.Goblin(slotIndex: 2, hp: 10);
        var s = Make(0, dead, alive1, alive2);
        var next = TargetingAutoSwitch.Apply(s);
        Assert.Equal(1, next.TargetEnemyIndex);
    }

    [Fact] public void All_dead_sets_target_to_null()
    {
        var dead0 = BattleFixtures.Goblin(0) with { CurrentHp = 0 };
        var dead1 = BattleFixtures.Goblin(1) with { CurrentHp = 0 };
        var s = Make(0, dead0, dead1);
        var next = TargetingAutoSwitch.Apply(s);
        Assert.Null(next.TargetEnemyIndex);
    }

    [Fact] public void Live_target_unchanged()
    {
        var s = Make(0, BattleFixtures.Goblin(0, 20));
        var next = TargetingAutoSwitch.Apply(s);
        Assert.Equal(0, next.TargetEnemyIndex);
    }

    [Fact] public void Null_target_remains_null()
    {
        var s = Make(null, BattleFixtures.Goblin(0, 20));
        var next = TargetingAutoSwitch.Apply(s);
        Assert.Null(next.TargetEnemyIndex);
    }
}
