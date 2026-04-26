using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class EnemyAttackingResolverStatusTests
{
    private static BattleState State(CombatActor hero, params CombatActor[] enemies) => new(
        Turn: 1, Phase: BattlePhase.EnemyAttacking,
        Outcome: RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending,
        Allies: ImmutableArray.Create(hero),
        Enemies: enemies.ToImmutableArray(),
        TargetAllyIndex: 0, TargetEnemyIndex: 0,
        Energy: 0, EnergyMax: 3,
        DrawPile: ImmutableArray<BattleCardInstance>.Empty,
        Hand: ImmutableArray<BattleCardInstance>.Empty,
        DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
        ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
        EncounterId: "enc_test");

    private static IRng Rng() => new FakeRng(new int[0], new double[0]);

    [Fact] public void Enemy_strength_boosts_per_effect_attack()
    {
        // 敵 attack 5 で、敵側 strength=3 → 1 effect で baseSum=5, addCount=1 → 5+1*3 = 8
        var hero = BattleFixtures.Hero(70);
        var goblin = BattleFixtures.WithStrength(BattleFixtures.Goblin(), 3);
        var def = BattleFixtures.GoblinDef(attack: 5);
        var catalog = BattleFixtures.MinimalCatalog(enemies: new[] { def });
        var s = State(hero, goblin);
        var (next, _) = EnemyAttackingResolver.Resolve(s, Rng(), catalog);
        Assert.Equal(70 - 8, next.Allies[0].CurrentHp);
    }

    [Fact] public void Enemy_weak_reduces_attack()
    {
        // 敵 attack 8 で、敵側 weak=1 → floor(8 * 0.75) = 6
        var hero = BattleFixtures.Hero(70);
        var goblin = BattleFixtures.WithWeak(BattleFixtures.Goblin(), 1);
        var def = BattleFixtures.GoblinDef(attack: 8);
        var catalog = BattleFixtures.MinimalCatalog(enemies: new[] { def });
        var s = State(hero, goblin);
        var (next, _) = EnemyAttackingResolver.Resolve(s, Rng(), catalog);
        Assert.Equal(70 - 6, next.Allies[0].CurrentHp);
    }

    [Fact] public void Hero_vulnerable_amplifies_damage()
    {
        // 敵 attack 10、hero vulnerable=1 → block 0 → rawDamage=10 → floor(10*1.5)=15
        var hero = BattleFixtures.WithVulnerable(BattleFixtures.Hero(70), 1);
        var goblin = BattleFixtures.Goblin();
        var def = BattleFixtures.GoblinDef(attack: 10);
        var catalog = BattleFixtures.MinimalCatalog(enemies: new[] { def });
        var s = State(hero, goblin);
        var (next, _) = EnemyAttackingResolver.Resolve(s, Rng(), catalog);
        Assert.Equal(70 - 15, next.Allies[0].CurrentHp);
    }

    [Fact] public void Hero_dexterity_boosts_block()
    {
        // 敵 attack 10、hero block Sum=2 AddCount=1 dex=5 → Display=7、absorbed=7、rawDamage=3
        var hero = BattleFixtures.WithDexterity(BattleFixtures.Hero(70), 5) with
        {
            Block = BlockPool.Empty.Add(2),
        };
        var goblin = BattleFixtures.Goblin();
        var def = BattleFixtures.GoblinDef(attack: 10);
        var catalog = BattleFixtures.MinimalCatalog(enemies: new[] { def });
        var s = State(hero, goblin);
        var (next, _) = EnemyAttackingResolver.Resolve(s, Rng(), catalog);
        Assert.Equal(70 - 3, next.Allies[0].CurrentHp);
    }
}
