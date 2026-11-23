using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class GuardianCombatRulesTests
{
    [Fact]
    public void Cover_uses_target_block_then_frontmost_guards_in_order()
    {
        var target = BattleFixtures.Hero(currentHp: 30, maxHp: 30, slotIndex: 0) with
        {
            Block = new BlockPool(2, 0),
        };
        var rearGuard = WithStatuses(
            BattleFixtures.SummonActor("rear", "guardian", 1, hp: 20),
            (BattleStatusIds.Cover, 1)) with { Block = new BlockPool(3, 0) };
        var frontGuard = WithStatuses(
            BattleFixtures.SummonActor("front", "guardian", 2, hp: 20),
            (BattleStatusIds.Cover, 1)) with { Block = new BlockPool(4, 0) };
        var enemy = BattleFixtures.Goblin();
        var state = State([target, rearGuard, frontGuard], enemy);

        var result = GuardianCombatRules.ApplyEnemyAttack(
            state, enemy, target.InstanceId, 8, "all", 0, 0);

        Assert.Equal(30, result.UpdatedTarget.CurrentHp);
        Assert.Equal(0, Find(result.State, target.InstanceId).Block.Display(0));
        Assert.Equal(1, Find(result.State, rearGuard.InstanceId).Block.Display(0));
        Assert.Equal(0, Find(result.State, frontGuard.InstanceId).Block.Display(0));
        Assert.Equal(rearGuard.InstanceId, result.FullyBlockingActorInstanceId);
        Assert.Equal(2, result.Events.Single(e =>
            e.Kind == BattleEventKind.DealDamage).BlockedAmount);
        Assert.Equal(
            [(frontGuard.InstanceId, 4), (rearGuard.InstanceId, 2)],
            result.Events
                .Where(e => e.Kind == BattleEventKind.CoverBlockSpent)
                .Select(e => (e.TargetInstanceId!, e.Amount!.Value)));
    }

    [Fact]
    public void Vulnerable_is_applied_only_after_target_and_cover_block()
    {
        var target = BattleFixtures.WithVulnerable(
            BattleFixtures.Hero(currentHp: 30, maxHp: 30), 1) with
        {
            Block = new BlockPool(2, 0),
        };
        var guard = WithStatuses(
            BattleFixtures.SummonActor("guard", "guardian", 1, hp: 20),
            (BattleStatusIds.Cover, 1)) with { Block = new BlockPool(2, 0) };
        var enemy = BattleFixtures.Goblin();

        var result = GuardianCombatRules.ApplyEnemyAttack(
            State([target, guard], enemy), enemy, target.InstanceId,
            10, "single", 0, 0);

        // (10 - target Block 2 - Cover Block 2) * 1.5 = 9.
        Assert.Equal(21, result.UpdatedTarget.CurrentHp);
        var damage = result.Events.Single(e => e.Kind == BattleEventKind.DealDamage);
        Assert.Equal(9, damage.Amount);
        Assert.Equal(2, damage.BlockedAmount);
        var cover = Assert.Single(result.Events, e =>
            e.Kind == BattleEventKind.CoverBlockSpent);
        Assert.Equal(guard.InstanceId, cover.TargetInstanceId);
        Assert.Equal(2, cover.Amount);
    }

    [Fact]
    public void Barrier_runs_after_all_block_and_does_not_refund_cover()
    {
        var target = WithStatuses(
            BattleFixtures.Hero(currentHp: 30, maxHp: 30),
            (BattleStatusIds.Barrier, 1)) with { Block = new BlockPool(2, 0) };
        var guard = WithStatuses(
            BattleFixtures.SummonActor("guard", "guardian", 1, hp: 20),
            (BattleStatusIds.Cover, 1)) with { Block = new BlockPool(1, 0) };
        var enemy = BattleFixtures.Goblin();

        var result = GuardianCombatRules.ApplyEnemyAttack(
            State([target, guard], enemy), enemy, target.InstanceId,
            5, "single", 0, 0);

        Assert.Equal(30, result.UpdatedTarget.CurrentHp);
        Assert.Equal(0, result.UpdatedTarget.GetStatus(BattleStatusIds.Barrier));
        Assert.Equal(0, result.UpdatedTarget.Block.Display(0));
        Assert.Equal(0, Find(result.State, guard.InstanceId).Block.Display(0));
        Assert.Equal(2, result.Events.Single(e =>
            e.Kind == BattleEventKind.DealDamage).BlockedAmount);
        Assert.Equal(1, result.Events.Single(e =>
            e.Kind == BattleEventKind.CoverBlockSpent).Amount);
    }

    [Fact]
    public void Defensive_stance_rewards_the_actor_that_completes_the_block()
    {
        var hero = WithStatuses(
            BattleFixtures.Hero(currentHp: 30, maxHp: 30),
            (BattleStatusIds.DefensiveStance, 1)) with
        {
            Block = new BlockPool(5, 0),
        };
        var enemy = BattleFixtures.Goblin();
        var state = State([hero], enemy);
        var resolution = GuardianCombatRules.ApplyEnemyAttack(
            state, enemy, hero.InstanceId, 3, "single", 0, 0);
        var events = resolution.Events.ToList();
        int order = events.Count;

        var next = GuardianCombatRules.AwardDefensiveStance(
            resolution.State,
            resolution.FullyBlockingActorInstanceId,
            new HashSet<string>(),
            0,
            events,
            ref order);

        Assert.Equal(1, next.ComboGauge);
        var gauge = Assert.Single(events, e =>
            e.Kind == BattleEventKind.ComboGaugeChanged);
        Assert.Equal(hero.InstanceId, gauge.CasterInstanceId);
        Assert.Equal(1, gauge.Amount);
    }

    [Fact]
    public void Defensive_stance_respects_stagnation()
    {
        var hero = WithStatuses(
            BattleFixtures.Hero(currentHp: 30, maxHp: 30),
            (BattleStatusIds.DefensiveStance, 1),
            (BattleStatusIds.Stagnation, 1)) with
        {
            Block = new BlockPool(5, 0),
        };
        var enemy = BattleFixtures.Goblin();
        var resolution = GuardianCombatRules.ApplyEnemyAttack(
            State([hero], enemy), enemy, hero.InstanceId,
            3, "single", 0, 0);
        var events = resolution.Events.ToList();
        int order = events.Count;

        var next = GuardianCombatRules.AwardDefensiveStance(
            resolution.State,
            resolution.FullyBlockingActorInstanceId,
            new HashSet<string>(),
            0,
            events,
            ref order);

        Assert.Equal(0, next.ComboGauge);
        Assert.DoesNotContain(events, e =>
            e.Kind == BattleEventKind.ComboGaugeChanged);
    }

    [Fact]
    public void Enemy_all_attack_rewards_same_guardian_only_once_per_hit()
    {
        var rearTarget = BattleFixtures.SummonActor(
            "rear", "ally", slotIndex: 0, hp: 20);
        var guardian = WithStatuses(
            BattleFixtures.Hero(currentHp: 30, maxHp: 30, slotIndex: 2),
            (BattleStatusIds.Cover, 1),
            (BattleStatusIds.DefensiveStance, 1)) with
        {
            Block = new BlockPool(6, 0),
        };
        var enemy = BattleFixtures.Goblin(moveId: "all");
        var definition = EnemyDefinition(
            new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 2));
        var state = BattleParticipantAccess.InitializePrimary(
            State([rearTarget, guardian], enemy));

        var (next, events) = EnemyAttackingResolver.Resolve(
            state, Rng(), BattleFixtures.MinimalCatalog(enemies: [definition]));

        Assert.Equal(1, BattleParticipantAccess.GetPrimary(next).ComboGauge);
        Assert.Single(events, e => e.Kind == BattleEventKind.ComboGaugeChanged);
        Assert.Equal(20, Find(next, rearTarget.InstanceId).CurrentHp);
        Assert.Equal(30, Find(next, guardian.InstanceId).CurrentHp);
    }

    [Fact]
    public void Enemy_multi_hit_can_reward_defensive_stance_once_per_hit()
    {
        var rearTarget = BattleFixtures.SummonActor(
            "rear", "ally", slotIndex: 0, hp: 20);
        var guardian = WithStatuses(
            BattleFixtures.Hero(currentHp: 30, maxHp: 30, slotIndex: 2),
            (BattleStatusIds.Cover, 1),
            (BattleStatusIds.DefensiveStance, 1)) with
        {
            Block = new BlockPool(4, 0),
        };
        var enemy = BattleFixtures.Goblin(moveId: "all");
        var definition = EnemyDefinition(
            new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 1),
            new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 1));
        var state = BattleParticipantAccess.InitializePrimary(
            State([rearTarget, guardian], enemy));

        var (next, events) = EnemyAttackingResolver.Resolve(
            state, Rng(), BattleFixtures.MinimalCatalog(enemies: [definition]));

        Assert.Equal(2, BattleParticipantAccess.GetPrimary(next).ComboGauge);
        Assert.Equal(2, events.Count(e =>
            e.Kind == BattleEventKind.ComboGaugeChanged));
    }

    [Fact]
    public void Steadfast_keeps_half_of_only_the_block_fortify_would_lose()
    {
        var actor = WithStatuses(
            BattleFixtures.Hero(),
            (BattleStatusIds.Fortify, 4),
            (BattleStatusIds.Steadfast, 2)) with
        {
            Block = new BlockPool(11, 0),
        };

        var next = GuardianTurnEndRules.ApplyBlockCarry(actor);

        // Fortify keeps 4, then Steadfast keeps floor((11 - 4) / 2) = 3.
        Assert.Equal(7, next.Block.Display(0));
    }

    [Fact]
    public void Guardian_countdown_runs_even_with_zero_block()
    {
        var actor = WithStatuses(
            BattleFixtures.Hero(),
            (BattleStatusIds.Steadfast, 1),
            (BattleStatusIds.Cover, 2),
            (BattleStatusIds.DefensiveStance, 1));

        var result = GuardianTurnEndRules.ApplyCountdown(
            actor, preserveCountdownBuffs: false);

        Assert.Equal(0, result.Actor.GetStatus(BattleStatusIds.Steadfast));
        Assert.Equal(1, result.Actor.GetStatus(BattleStatusIds.Cover));
        Assert.Equal(0, result.Actor.GetStatus(BattleStatusIds.DefensiveStance));
        Assert.Equal(2, result.Events.Count);
    }

    [Fact]
    public void Afterglow_can_preserve_all_guardian_countdowns()
    {
        var actor = WithStatuses(
            BattleFixtures.Hero(),
            (BattleStatusIds.Steadfast, 1),
            (BattleStatusIds.Cover, 2),
            (BattleStatusIds.DefensiveStance, 1));

        var result = GuardianTurnEndRules.ApplyCountdown(
            actor, preserveCountdownBuffs: true);

        Assert.Equal(actor, result.Actor);
        Assert.Empty(result.Events);
    }

    [Fact]
    public void Cover_remains_active_through_enemy_attacking_then_counts_down()
    {
        var protectedAlly = BattleFixtures.SummonActor(
            "protected", "ally", slotIndex: 0, hp: 10);
        var guardian = WithStatuses(
            BattleFixtures.Hero(currentHp: 30, maxHp: 30, slotIndex: 2),
            (BattleStatusIds.Cover, 1)) with
        {
            Block = new BlockPool(5, 0),
        };
        var enemy = BattleFixtures.Goblin();
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(protectedAlly, guardian),
            enemies: ImmutableArray.Create(enemy),
            phase: BattlePhase.PlayerInput);

        var (next, _) = BattleEngine.EndTurn(
            state, Rng(), BattleFixtures.MinimalCatalog());

        Assert.Equal(10, Find(next, protectedAlly.InstanceId).CurrentHp);
        Assert.Equal(25, Find(next, guardian.InstanceId).CurrentHp);
        Assert.Equal(0, Find(next, guardian.InstanceId).GetStatus(BattleStatusIds.Cover));
    }

    private static CombatActor WithStatuses(
        CombatActor actor,
        params (string id, int amount)[] statuses)
    {
        foreach (var (id, amount) in statuses)
            actor = BattleFixtures.WithStatus(actor, id, amount);
        return actor;
    }

    private static BattleState State(
        CombatActor[] allies,
        CombatActor enemy) =>
        BattleFixtures.MinimalState(
            allies: allies.ToImmutableArray(),
            enemies: ImmutableArray.Create(enemy),
            phase: BattlePhase.EnemyAttacking);

    private static CombatActor Find(BattleState state, string instanceId) =>
        state.Allies.Single(actor => actor.InstanceId == instanceId);

    private static EnemyDefinition EnemyDefinition(params CardEffect[] effects) =>
        new(
            "goblin",
            "Goblin",
            "img_goblin",
            20,
            new EnemyPool(1, EnemyTier.Weak),
            "all",
            [new MoveDefinition("all", MoveKind.Attack, effects, "all")]);

    private static IRng Rng() =>
        new FakeRng(new int[20], System.Array.Empty<double>());
}
