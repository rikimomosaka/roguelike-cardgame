using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class PlayerAttackingAddedAttackTests
{
    [Fact]
    public void Resolves_only_hits_after_each_snapshot_count()
    {
        var hero = BattleFixtures.Hero() with
        {
            AttackSingle = AttackPool.Empty.Add(3).Add(5),
        };
        var enemy = BattleFixtures.Goblin(hp: 20);

        var (next, events) = PlayerAttackingResolver.ResolveAddedAttacks(
            State(hero, enemy), hero.InstanceId,
            beforeSingleCount: 1,
            beforeRandomCount: 0,
            beforeAllCount: 0,
            Rng(), BattleFixtures.MinimalCatalog(), orderStart: 7);

        Assert.Equal(15, next.Enemies[0].CurrentHp);
        var damage = Assert.Single(events, e => e.Kind == BattleEventKind.DealDamage);
        Assert.Equal(8, damage.Order);
        Assert.Equal(5, damage.Amount);
    }

    [Fact]
    public void Added_attacks_do_not_fire_pursuit_again()
    {
        var hero = BattleFixtures.WithStatus(
            BattleFixtures.Hero(), BattleStatusIds.Pursuit, 4) with
        {
            AttackAll = AttackPool.Empty.Add(2),
        };
        var enemy = BattleFixtures.Goblin(hp: 20);

        var (next, _) = PlayerAttackingResolver.ResolveAddedAttacks(
            State(hero, enemy), hero.InstanceId,
            0, 0, 0, Rng(), BattleFixtures.MinimalCatalog(), 0);

        Assert.Equal(18, next.Enemies[0].CurrentHp);
        Assert.Equal(4, next.Allies[0].GetStatus(BattleStatusIds.Pursuit));
    }

    [Fact]
    public void Omnistrike_has_priority_over_disruption_for_added_attacks()
    {
        var hero = BattleFixtures.WithStatus(
            BattleFixtures.WithStatus(
                BattleFixtures.Hero(), BattleStatusIds.Omnistrike, 1),
            BattleStatusIds.Disruption, 1) with
        {
            AttackSingle = AttackPool.Empty.Add(2),
        };
        var enemies = new[]
        {
            BattleFixtures.Goblin(slotIndex: 0, hp: 20),
            BattleFixtures.Goblin(slotIndex: 1, hp: 20),
        };

        var (next, events) = PlayerAttackingResolver.ResolveAddedAttacks(
            State(hero, enemies), hero.InstanceId,
            0, 0, 0, Rng(1), BattleFixtures.MinimalCatalog(), 0);

        Assert.All(next.Enemies, enemy => Assert.Equal(18, enemy.CurrentHp));
        Assert.Equal(2, events.Count(e => e.Kind == BattleEventKind.DealDamage));
    }

    [Fact]
    public void Disruption_randomizes_each_added_attack()
    {
        var hero = BattleFixtures.WithStatus(
            BattleFixtures.Hero(), BattleStatusIds.Disruption, 1) with
        {
            AttackAll = AttackPool.Empty.Add(2),
        };
        var enemies = new[]
        {
            BattleFixtures.Goblin(slotIndex: 0, hp: 20),
            BattleFixtures.Goblin(slotIndex: 1, hp: 20),
        };

        var (next, events) = PlayerAttackingResolver.ResolveAddedAttacks(
            State(hero, enemies), hero.InstanceId,
            0, 0, 0, Rng(1), BattleFixtures.MinimalCatalog(), 0);

        Assert.Equal(20, next.Enemies[0].CurrentHp);
        Assert.Equal(18, next.Enemies[1].CurrentHp);
        Assert.Single(events, e => e.Kind == BattleEventKind.DealDamage);
    }

    private static BattleState State(CombatActor hero, params CombatActor[] enemies) =>
        BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero),
            enemies: enemies.ToImmutableArray(),
            phase: BattlePhase.PlayerAttacking);

    private static IRng Rng(params int[] values) =>
        new FakeRng(
            values.Length == 0 ? new int[20] : values,
            System.Array.Empty<double>());
}
