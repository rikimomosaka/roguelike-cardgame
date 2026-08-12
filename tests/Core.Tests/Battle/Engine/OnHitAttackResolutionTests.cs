using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class OnHitAttackResolutionTests
{
    [Fact]
    public void Power_OnHit_attack_resolves_immediately_with_a_new_hit_index_and_full_cause_chain()
    {
        var power = OnHitAttackPower("echo_blade", 3);
        var hero = BattleFixtures.Hero() with
        {
            AttackSingle = AttackPool.Empty.Add(2),
        };
        var state = PlayerAttackState(hero, BattleFixtures.Goblin(hp: 20)) with
        {
            PowerCards = ImmutableArray.Create(
                BattleFixtures.MakeBattleCard(power.Id, "echo-blade-p0")),
        };

        var (next, events) = PlayerAttackingResolver.Resolve(
            state, new FakeRng([], []),
            BattleFixtures.MinimalCatalog(cards: new[] { power }));

        Assert.Equal(15, next.Enemies[0].CurrentHp);
        Assert.Equal(1, next.Allies[0].AttackSingle.AddCount);

        var attacks = events
            .Where(e => e.Kind == BattleEventKind.DealDamage &&
                        e.DamageType == DamageType.Attack)
            .ToList();
        Assert.Equal(2, attacks.Count);
        Assert.Equal(0, attacks[0].HitIndex);
        Assert.Equal(1, attacks[1].HitIndex);

        var child = attacks[1];
        Assert.Equal(CombatSourceKind.PowerEffect, child.Source?.Kind);
        Assert.Equal(power.Id, child.Source?.DefinitionId);
        Assert.Equal(3, child.CauseChain?.Length);
        Assert.Equal(new[] { "attack", "OnHit", "attack" },
            child.CauseChain?.Frames.Select(frame => frame.Cause));
        Assert.Equal(new int?[] { 0, 0, 1 },
            child.CauseChain?.Frames.Select(frame => frame.HitIndex));
    }

    [Fact]
    public void OnHit_derived_attack_does_not_fire_OnHit_again()
    {
        var attackRelic = BattleFixtures.Relic(
            id: "echo_relic", trigger: "OnHit", implemented: true,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 2,
                Trigger: "OnHit", ChancePercent: 100));
        var weakPower = OnHitDebuffPower("one_weak", 1);
        var hero = BattleFixtures.Hero() with
        {
            AttackSingle = AttackPool.Empty.Add(2),
        };
        var state = PlayerAttackState(hero, BattleFixtures.Goblin(hp: 20)) with
        {
            OwnedRelicIds = ImmutableArray.Create(attackRelic.Id),
            PowerCards = ImmutableArray.Create(
                BattleFixtures.MakeBattleCard(weakPower.Id, "one-weak-p0")),
        };

        var (next, events) = PlayerAttackingResolver.Resolve(
            state, new FakeRng([], []),
            BattleFixtures.MinimalCatalog(
                cards: new[] { weakPower }, relics: new[] { attackRelic }));

        Assert.Equal(16, next.Enemies[0].CurrentHp);
        Assert.Equal(1, next.Enemies[0].GetStatus("weak"));
        Assert.Equal(2, events.Count(e =>
            e.Kind == BattleEventKind.DealDamage && e.DamageType == DamageType.Attack));
        Assert.Single(events, e => e.Kind == BattleEventKind.ApplyStatus);
    }

    [Fact]
    public void OnHit_child_is_a_normal_hit_for_barrier_and_reflect()
    {
        var power = OnHitAttackPower("barrier_breaker", 3);
        var hero = BattleFixtures.Hero() with
        {
            AttackSingle = AttackPool.Empty.Add(1),
        };
        var enemy = BattleFixtures.WithStatus(
            BattleFixtures.WithStatus(BattleFixtures.Goblin(hp: 20), "barrier", 1),
            "reflect", 2);
        var state = PlayerAttackState(hero, enemy) with
        {
            PowerCards = ImmutableArray.Create(
                BattleFixtures.MakeBattleCard(power.Id, "barrier-breaker-p0")),
        };

        var (next, events) = PlayerAttackingResolver.Resolve(
            state, new FakeRng([], []),
            BattleFixtures.MinimalCatalog(cards: new[] { power }));

        Assert.Equal(17, next.Enemies[0].CurrentHp);
        Assert.Equal(0, next.Enemies[0].GetStatus("barrier"));
        Assert.Equal(66, next.Allies[0].CurrentHp);
        Assert.Equal(2, events.Count(e =>
            e.Kind == BattleEventKind.DealDamage && e.DamageType == DamageType.Reflected));
        Assert.Contains(events, e => e.Kind == BattleEventKind.DealDamage &&
            e.DamageType == DamageType.Attack && e.HitIndex == 1 && e.ActualHpLoss == 3);
    }

    [Fact]
    public void OnHit_child_kill_fires_OnEnemyDeath_exactly_once()
    {
        var attackRelic = BattleFixtures.Relic(
            id: "finisher", trigger: "OnHit", implemented: true,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 4,
                Trigger: "OnHit", ChancePercent: 100));
        var deathRelic = BattleFixtures.Relic(
            id: "kill_reward", trigger: "OnEnemyDeath", implemented: true,
            new CardEffect("buff", EffectScope.Self, null, 1,
                Name: "strength", Trigger: "OnEnemyDeath"));
        var hero = BattleFixtures.Hero() with
        {
            AttackSingle = AttackPool.Empty.Add(1),
        };
        var state = PlayerAttackState(hero, BattleFixtures.Goblin(hp: 4)) with
        {
            OwnedRelicIds = ImmutableArray.Create(attackRelic.Id, deathRelic.Id),
        };

        var (next, events) = PlayerAttackingResolver.Resolve(
            state, new FakeRng([], []),
            BattleFixtures.MinimalCatalog(relics: new[] { attackRelic, deathRelic }));

        Assert.False(next.Enemies[0].IsAlive);
        Assert.Equal(1, next.Allies[0].GetStatus("strength"));
        Assert.Single(events, e => e.Kind == BattleEventKind.ApplyStatus &&
            e.Note!.Contains("relic:kill_reward"));
        var death = Assert.Single(events, e => e.Kind == BattleEventKind.ActorDeath &&
            e.TargetInstanceId == next.Enemies[0].InstanceId);
        Assert.Equal(1, death.HitIndex);
        Assert.Equal(3, death.CauseChain?.Length);
    }

    [Fact]
    public void All_root_events_stay_contiguous_before_per_target_OnHit_children_and_reflect()
    {
        var power = OnHitAttackPower("all_echo", 3);
        var hero = BattleFixtures.Hero() with
        {
            AttackAll = AttackPool.Empty.Add(2),
        };
        var enemyA = BattleFixtures.WithStatus(
            BattleFixtures.Goblin(slotIndex: 0, hp: 10), "reflect", 2);
        var enemyB = BattleFixtures.WithStatus(
            BattleFixtures.Goblin(slotIndex: 1, hp: 10), "reflect", 2);
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero),
            enemies: ImmutableArray.Create(enemyA, enemyB)) with
        {
            Phase = BattlePhase.PlayerAttacking,
            PowerCards = ImmutableArray.Create(
                BattleFixtures.MakeBattleCard(power.Id, "all-echo-p0")),
        };

        var (next, events) = PlayerAttackingResolver.Resolve(
            state, new FakeRng([], []),
            BattleFixtures.MinimalCatalog(cards: new[] { power }));

        Assert.Collection(
            events.Take(4),
            e => AssertRootAllEvent(e, BattleEventKind.AttackFire, enemyA.InstanceId),
            e => AssertRootAllEvent(e, BattleEventKind.DealDamage, enemyA.InstanceId),
            e => AssertRootAllEvent(e, BattleEventKind.AttackFire, enemyB.InstanceId),
            e => AssertRootAllEvent(e, BattleEventKind.DealDamage, enemyB.InstanceId));

        var childFires = events.Where(e =>
            e.Kind == BattleEventKind.AttackFire && e.HitIndex is 1 or 2).ToList();
        Assert.Equal(2, childFires.Count);
        Assert.Equal(new[] { enemyA.InstanceId, enemyB.InstanceId },
            childFires.Select(e => e.TargetInstanceId));
        Assert.Equal(new int?[] { 1, 2 }, childFires.Select(e => e.HitIndex));
        Assert.All(childFires, e =>
            Assert.Equal(new[] { "attack", "OnHit", "attack" },
                e.CauseChain?.Frames.Select(frame => frame.Cause)));

        Assert.All(next.Enemies, enemy => Assert.Equal(5, enemy.CurrentHp));
        Assert.Equal(62, next.Allies[0].CurrentHp);
        Assert.Equal(4, events.Count(e =>
            e.Kind == BattleEventKind.DealDamage && e.DamageType == DamageType.Reflected));
    }

    private static CardDefinition OnHitAttackPower(string id, int amount) => new(
        id, id, null, CardRarity.Common, CardType.Power, 1, null,
        new[]
        {
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, amount,
                Trigger: "OnHit", ChancePercent: 100),
        },
        null, null);

    private static CardDefinition OnHitDebuffPower(string id, int amount) => new(
        id, id, null, CardRarity.Common, CardType.Power, 1, null,
        new[]
        {
            new CardEffect("debuff", EffectScope.Single, EffectSide.Enemy, amount,
                Name: "weak", Trigger: "OnHit", ChancePercent: 100),
        },
        null, null);

    private static void AssertRootAllEvent(
        BattleEvent battleEvent, BattleEventKind expectedKind, string expectedTargetId)
    {
        Assert.Equal(expectedKind, battleEvent.Kind);
        Assert.Equal(expectedTargetId, battleEvent.TargetInstanceId);
        Assert.Equal(0, battleEvent.HitIndex);
        Assert.Equal(DamageType.Attack, battleEvent.DamageType);
        Assert.Equal("all", battleEvent.Note);
    }

    private static BattleState PlayerAttackState(CombatActor hero, CombatActor enemy) =>
        BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero),
            enemies: ImmutableArray.Create(enemy)) with
        {
            Phase = BattlePhase.PlayerAttacking,
        };
}
