using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.Projection;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public sealed class DisruptionRuntimeTests
{
    [Fact]
    public void Queued_single_and_all_hits_each_choose_one_random_target()
    {
        var hero = WithStatus(
            BattleFixtures.Hero() with
            {
                AttackSingle = AttackPool.Empty.Add(3),
                AttackAll = AttackPool.Empty.Add(4),
            },
            BattleStatusIds.Disruption,
            1);
        var first = BattleFixtures.Goblin(0, hp: 20);
        var second = BattleFixtures.Goblin(1, hp: 20);
        var state = BattleFixtures.MinimalState(
            allies: [hero],
            enemies: [first, second],
            phase: BattlePhase.PlayerAttacking);

        var (after, events) = PlayerAttackingResolver.Resolve(
            state,
            new FakeRng([1, 0], []),
            BattleFixtures.MinimalCatalog());

        Assert.Equal(16, after.Enemies[0].CurrentHp);
        Assert.Equal(17, after.Enemies[1].CurrentHp);
        Assert.Equal(
            [second.InstanceId, first.InstanceId],
            events.Where(battleEvent => battleEvent.Kind == BattleEventKind.AttackFire)
                .Select(battleEvent => battleEvent.TargetInstanceId));
        Assert.All(
            events.Where(battleEvent => battleEvent.Kind == BattleEventKind.AttackFire),
            battleEvent => Assert.Equal("random", battleEvent.Note));
    }

    [Fact]
    public void Omnistrike_wins_over_disruption_for_queued_attacks()
    {
        var hero = BattleFixtures.Hero() with
        {
            AttackSingle = AttackPool.Empty.Add(3),
            Statuses = ImmutableDictionary<string, int>.Empty
                .Add(BattleStatusIds.Disruption, 1)
                .Add(BattleStatusIds.Omnistrike, 1),
        };
        var state = BattleFixtures.MinimalState(
            allies: [hero],
            enemies:
            [
                BattleFixtures.Goblin(0, hp: 20),
                BattleFixtures.Goblin(1, hp: 20),
            ],
            phase: BattlePhase.PlayerAttacking);

        var (after, _) = PlayerAttackingResolver.Resolve(
            state,
            new FakeRng([], []),
            BattleFixtures.MinimalCatalog());

        Assert.All(after.Enemies, enemy => Assert.Equal(17, enemy.CurrentHp));
    }

    [Fact]
    public void Enemy_all_attack_becomes_one_random_target()
    {
        var move = new MoveDefinition(
            "swing",
            MoveKind.Attack,
            [new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 5)],
            "swing");
        var definition = new EnemyDefinition(
            "disrupted_enemy",
            "Disrupted",
            "disrupted_enemy",
            30,
            new EnemyPool(1, EnemyTier.Weak),
            move.Id,
            [move]);
        var enemy = WithStatus(
            BattleFixtures.Goblin(moveId: move.Id) with
            {
                DefinitionId = definition.Id,
            },
            BattleStatusIds.Disruption,
            1);
        var hero = BattleFixtures.Hero();
        var ally = BattleFixtures.SummonActor("ally", "minion", 1, hp: 20);
        var state = BattleFixtures.MinimalState(
            allies: [hero, ally],
            enemies: [enemy],
            phase: BattlePhase.EnemyAttacking);
        var catalog = BattleFixtures.MinimalCatalog(enemies: [definition]);

        var (after, events) = EnemyAttackingResolver.Resolve(
            state,
            new FakeRng([99, 1], []),
            catalog);

        Assert.Equal(hero.MaxHp, after.Allies[0].CurrentHp);
        Assert.Equal(15, after.Allies[1].CurrentHp);
        var attack = Assert.Single(events, battleEvent =>
            battleEvent.Kind == BattleEventKind.AttackFire);
        Assert.Equal(ally.InstanceId, attack.TargetInstanceId);
        Assert.Equal("random", attack.Note);
    }

    [Fact]
    public void Fixed_unit_all_attack_becomes_one_random_target()
    {
        var move = new MoveDefinition(
            "burst",
            MoveKind.Attack,
            [new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 4)],
            "burst");
        var unit = new UnitDefinition(
            "disrupted_unit",
            "Disrupted Unit",
            "disrupted_unit",
            15,
            move.Id,
            [move]);
        var summon = WithStatus(
            BattleFixtures.SummonActor(
                "unit",
                unit.Id,
                1,
                hp: 15,
                moveId: move.Id),
            BattleStatusIds.Disruption,
            1);
        var first = BattleFixtures.Goblin(0, hp: 20);
        var second = BattleFixtures.Goblin(1, hp: 20);
        var state = BattleFixtures.MinimalState(
            allies: [BattleFixtures.Hero(), summon],
            enemies: [first, second],
            phase: BattlePhase.PlayerAttacking);
        var catalog = BattleFixtures.MinimalCatalog(units: [unit]);

        var (after, events) = PlayerAttackingResolver.Resolve(
            state,
            new FakeRng([99, 1], []),
            catalog);

        Assert.Equal(20, after.Enemies[0].CurrentHp);
        Assert.Equal(16, after.Enemies[1].CurrentHp);
        var attack = Assert.Single(events, battleEvent =>
            battleEvent.Kind == BattleEventKind.AttackFire);
        Assert.Equal(second.InstanceId, attack.TargetInstanceId);
        Assert.Equal("random", attack.Note);
    }

    [Fact]
    public void Intent_projection_uses_random_scope_and_preserves_omnistrike_priority()
    {
        var move = new MoveDefinition(
            "mixed",
            MoveKind.Multi,
            [
                new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 3),
                new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 4),
            ],
            "mixed");
        var definition = new EnemyDefinition(
            "intent_disrupted",
            "Intent Disrupted",
            "intent_disrupted",
            30,
            new EnemyPool(1, EnemyTier.Weak),
            move.Id,
            [move]);
        var disrupted = WithStatus(
            BattleFixtures.Goblin(moveId: move.Id) with
            {
                DefinitionId = definition.Id,
            },
            BattleStatusIds.Disruption,
            1);
        var state = BattleFixtures.MinimalState(enemies: [disrupted]);
        var catalog = BattleFixtures.MinimalCatalog(enemies: [definition]);

        var randomIntent = CombatIntentProjector.Project(state, disrupted, catalog);
        var omni = WithStatus(disrupted, BattleStatusIds.Omnistrike, 1);
        var allIntent = CombatIntentProjector.Project(
            state with { Enemies = [omni] },
            omni,
            catalog);

        Assert.NotNull(randomIntent);
        Assert.Null(randomIntent!.AttackSingle);
        Assert.Equal(7, randomIntent.AttackRandom);
        Assert.Equal(2, randomIntent.AttackRandomHits);
        Assert.Null(randomIntent.AttackAll);
        Assert.NotNull(allIntent);
        Assert.Null(allIntent!.AttackRandom);
        Assert.Equal(7, allIntent.AttackAll);
        Assert.Equal(2, allIntent.AttackAllHits);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 10)]
    [InlineData(3, 30)]
    [InlineData(5, 50)]
    [InlineData(6, 50)]
    [InlineData(99, 50)]
    public void Failure_percent_is_ten_per_stack_capped_at_fifty(int disruption, int expected)
    {
        Assert.Equal(expected, DisruptionRules.FailurePercent(disruption));
    }

    [Fact]
    public void Disrupted_card_play_can_fail_and_keeps_cost_but_skips_effects()
    {
        var hero = WithStatus(BattleFixtures.Hero(), BattleStatusIds.Disruption, 3);
        var state = BattleFixtures.MinimalState(
            allies: [hero],
            hand: [BattleFixtures.MakeBattleCard("defend", "c1")]);

        // 0 < 30% → 失敗
        var (after, events) = BattleEngine.PlayCard(
            state, 0, null, null, new FakeRng([0], []), BattleFixtures.MinimalCatalog());

        Assert.Equal(0, after.Allies[0].Block.Display(0));
        Assert.Equal(2, after.Energy);
        Assert.Empty(after.Hand);
        Assert.Single(after.DiscardPile);
        var failed = Assert.Single(events, e => e.Kind == BattleEventKind.ActionFailed);
        Assert.Equal(hero.InstanceId, failed.CasterInstanceId);
        Assert.Equal("defend", failed.CardId);
        Assert.Equal(30, failed.Amount);
        Assert.DoesNotContain(events, e => e.Kind == BattleEventKind.GainBlock);
    }

    [Fact]
    public void Disrupted_card_play_succeeds_when_roll_is_at_or_above_the_rate()
    {
        var hero = WithStatus(BattleFixtures.Hero(), BattleStatusIds.Disruption, 3);
        var state = BattleFixtures.MinimalState(
            allies: [hero],
            hand: [BattleFixtures.MakeBattleCard("defend", "c1")]);

        var (after, events) = BattleEngine.PlayCard(
            state, 0, null, null, new FakeRng([30], []), BattleFixtures.MinimalCatalog());

        Assert.True(after.Allies[0].Block.Display(0) > 0);
        Assert.DoesNotContain(events, e => e.Kind == BattleEventKind.ActionFailed);
    }

    [Fact]
    public void Card_play_without_disruption_draws_no_random_number()
    {
        var state = BattleFixtures.MinimalState(
            hand: [BattleFixtures.MakeBattleCard("defend", "c1")]);

        // 乱数を 1 つでも引くと FakeRng が例外を投げる。
        var (after, _) = BattleEngine.PlayCard(
            state, 0, null, null, new FakeRng([], []), BattleFixtures.MinimalCatalog());

        Assert.True(after.Allies[0].Block.Display(0) > 0);
    }

    [Fact]
    public void Disrupted_enemy_move_can_fail_entirely()
    {
        var move = new MoveDefinition(
            "swing",
            MoveKind.Attack,
            [new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 5)],
            "swing");
        var definition = new EnemyDefinition(
            "disrupted_enemy", "Disrupted", "disrupted_enemy", 30,
            new EnemyPool(1, EnemyTier.Weak), move.Id, [move]);
        var enemy = WithStatus(
            BattleFixtures.Goblin(moveId: move.Id) with { DefinitionId = definition.Id },
            BattleStatusIds.Disruption,
            6);
        var hero = BattleFixtures.Hero();
        var state = BattleFixtures.MinimalState(
            allies: [hero], enemies: [enemy], phase: BattlePhase.EnemyAttacking);

        // 攪乱 6 でも失敗率は 50%。49 < 50 → 失敗。
        var (after, events) = EnemyAttackingResolver.Resolve(
            state, new FakeRng([49], []), BattleFixtures.MinimalCatalog(enemies: [definition]));

        Assert.Equal(hero.MaxHp, after.Allies[0].CurrentHp);
        var failed = Assert.Single(events, e => e.Kind == BattleEventKind.ActionFailed);
        Assert.Equal(enemy.InstanceId, failed.CasterInstanceId);
        Assert.Equal(50, failed.Amount);
        Assert.DoesNotContain(events, e => e.Kind == BattleEventKind.AttackFire);
    }

    private static CombatActor WithStatus(
        CombatActor actor,
        string id,
        int amount) => actor with
    {
        Statuses = actor.Statuses.SetItem(id, amount),
    };
}
