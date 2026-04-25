using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class EnemyAttackingResolverTests
{
    private static BattleState MakeState(CombatActor hero, params CombatActor[] enemies) => new(
        Turn: 1, Phase: BattlePhase.EnemyAttacking, Outcome: BattleOutcome.Pending,
        Allies: ImmutableArray.Create(hero),
        Enemies: enemies.ToImmutableArray(),
        TargetAllyIndex: 0, TargetEnemyIndex: 0,
        Energy: 0, EnergyMax: 3,
        DrawPile: ImmutableArray<BattleCardInstance>.Empty,
        Hand: ImmutableArray<BattleCardInstance>.Empty,
        DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
        ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
        EncounterId: "enc_test");

    private static IRng Rng() => new FakeRng(new int[10], new double[0]);

    [Fact] public void Enemy_attack_scope_all_hits_hero()
    {
        var hero = BattleFixtures.Hero();
        var goblin = BattleFixtures.Goblin();
        var s = MakeState(hero, goblin);
        var cat = BattleFixtures.MinimalCatalog(
            enemies: new[] { BattleFixtures.GoblinDef(hp: 20, attack: 5) });
        var (next, events) = EnemyAttackingResolver.Resolve(s, Rng(), cat);
        Assert.Equal(65, next.Allies[0].CurrentHp); // 70 - 5
        Assert.Contains(events, e => e.Kind == BattleEventKind.DealDamage && e.Amount == 5);
    }

    [Fact] public void Per_effect_immediate_fire_with_two_attacks()
    {
        var twoHits = new EnemyDefinition(
            "twohit", "Two Hit", "img", 30, new EnemyPool(1, EnemyTier.Weak), "double",
            new[] {
                new MoveDefinition("double", MoveKind.Attack,
                    new[] {
                        new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 3),
                        new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 3),
                    },
                    "double")
            });
        var hero = BattleFixtures.Hero();
        var enemy = new CombatActor("e1", "twohit", ActorSide.Enemy, 0, 30, 30,
            BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty, "double");
        var s = MakeState(hero, enemy);
        var cat = BattleFixtures.MinimalCatalog(enemies: new[] { twoHits });
        var (next, events) = EnemyAttackingResolver.Resolve(s, Rng(), cat);
        // 2 回 × 3 ダメージ = 6
        Assert.Equal(64, next.Allies[0].CurrentHp);
        var dealCount = events.Count(e => e.Kind == BattleEventKind.DealDamage);
        Assert.Equal(2, dealCount);
    }

    [Fact] public void Enemy_block_self_increments_own_block()
    {
        var defender = new EnemyDefinition(
            "defender", "Defender", "img", 30, new EnemyPool(1, EnemyTier.Weak), "guard",
            new[] {
                new MoveDefinition("guard", MoveKind.Defend,
                    new[] { new CardEffect("block", EffectScope.Self, null, 5) },
                    "guard")
            });
        var hero = BattleFixtures.Hero();
        var enemy = new CombatActor("e1", "defender", ActorSide.Enemy, 0, 30, 30,
            BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty, "guard");
        var s = MakeState(hero, enemy);
        var cat = BattleFixtures.MinimalCatalog(enemies: new[] { defender });
        var (next, _) = EnemyAttackingResolver.Resolve(s, Rng(), cat);
        Assert.Equal(5, next.Enemies[0].Block.Sum);
    }

    [Fact] public void Enemy_transitions_to_NextMoveId()
    {
        var moveA = new MoveDefinition("a", MoveKind.Attack,
            new[] { new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 1) },
            "b");
        var moveB = new MoveDefinition("b", MoveKind.Attack,
            new[] { new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 1) },
            "a");
        var def = new EnemyDefinition(
            "alt", "Alt", "img", 30, new EnemyPool(1, EnemyTier.Weak), "a",
            new[] { moveA, moveB });
        var hero = BattleFixtures.Hero();
        var enemy = new CombatActor("e1", "alt", ActorSide.Enemy, 0, 30, 30,
            BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty, "a");
        var s = MakeState(hero, enemy);
        var cat = BattleFixtures.MinimalCatalog(enemies: new[] { def });
        var (next, _) = EnemyAttackingResolver.Resolve(s, Rng(), cat);
        Assert.Equal("b", next.Enemies[0].CurrentMoveId);
    }

    [Fact] public void Dead_enemies_skip_action()
    {
        var hero = BattleFixtures.Hero();
        var dead = BattleFixtures.Goblin() with { CurrentHp = 0 };
        var s = MakeState(hero, dead);
        var cat = BattleFixtures.MinimalCatalog();
        var (next, events) = EnemyAttackingResolver.Resolve(s, Rng(), cat);
        Assert.Equal(70, next.Allies[0].CurrentHp);
        Assert.DoesNotContain(events, e => e.Kind == BattleEventKind.AttackFire);
    }
}
