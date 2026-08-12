using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.Projection;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;

namespace RoguelikeCardGame.Core.Tests.Battle.Projection;

public sealed class CombatIntentProjectorTests
{
    [Fact]
    public void Enemy_self_strength_before_attack_is_reflected_and_matches_resolution()
    {
        var move = new MoveDefinition(
            "prepare_and_strike",
            MoveKind.Multi,
            new CardEffect[]
            {
                new("buff", EffectScope.Self, null, 2, Name: "strength"),
                new("attack", EffectScope.All, EffectSide.Enemy, 10),
            },
            "prepare_and_strike");
        var enemyDefinition = Enemy("sequencer", move);
        var enemy = BattleFixtures.Goblin(moveId: move.Id) with
        {
            DefinitionId = enemyDefinition.Id,
        };
        var state = BattleFixtures.MinimalState(
            enemies: ImmutableArray.Create(enemy));
        var catalog = BattleFixtures.MinimalCatalog(
            enemies: new[] { enemyDefinition });

        var intent = CombatIntentProjector.Project(state, enemy, catalog);

        Assert.NotNull(intent);
        Assert.True(intent!.HasBuff);
        Assert.Equal(12, intent.AttackAll);
        Assert.Equal(1, intent.AttackHits);
        Assert.Equal(1, intent.AttackAllHits);

        var (resolved, events) = EnemyAttackingResolver.Resolve(
            state, new FakeRng([], []), catalog);
        var attack = Assert.Single(events, battleEvent =>
            battleEvent.Kind == BattleEventKind.AttackFire);
        Assert.Equal(intent.AttackAll, attack.Amount);
        Assert.Equal(2, resolved.Enemies[0].GetStatus("strength"));
        Assert.Empty(enemy.Statuses);
    }

    [Fact]
    public void Move_effect_order_applies_strength_and_weak_per_later_hit()
    {
        var move = new MoveDefinition(
            "ordered",
            MoveKind.Multi,
            new CardEffect[]
            {
                new("attack", EffectScope.Single, EffectSide.Enemy, 10),
                new("buff", EffectScope.Self, null, 2, Name: "strength"),
                new("attack", EffectScope.Single, EffectSide.Enemy, 10),
            },
            "ordered");
        var definition = Enemy("ordered_enemy", move);
        var actor = BattleFixtures.WithWeak(
            BattleFixtures.Goblin(moveId: move.Id) with
            {
                DefinitionId = definition.Id,
            });
        var state = BattleFixtures.MinimalState(
            enemies: ImmutableArray.Create(actor));
        var catalog = BattleFixtures.MinimalCatalog(
            enemies: new[] { definition });

        var intent = CombatIntentProjector.Project(state, actor, catalog);

        Assert.NotNull(intent);
        // floor(10 * .75) + floor((10 + 2) * .75) = 7 + 9.
        Assert.Equal(16, intent!.AttackSingle);
        Assert.Equal(2, intent.AttackSingleHits);
        Assert.Equal(2, intent.AttackHits);
        Assert.Equal(1, actor.GetStatus("weak"));
        Assert.Equal(0, actor.GetStatus("strength"));
    }

    [Fact]
    public void Move_projection_preserves_each_scope_hit_count_and_non_attack_flags()
    {
        var move = new MoveDefinition(
            "mixed",
            MoveKind.Multi,
            new CardEffect[]
            {
                new("attack", EffectScope.Single, EffectSide.Enemy, 3),
                new("attack", EffectScope.Random, EffectSide.Enemy, 4),
                new("attack", EffectScope.All, EffectSide.Enemy, 5),
                new("attack", EffectScope.All, EffectSide.Enemy, 6),
                new("block", EffectScope.Self, null, 7),
                new("buff", EffectScope.Self, null, 1, Name: "strength"),
                new("debuff", EffectScope.All, EffectSide.Enemy, 1, Name: "weak"),
                new("heal", EffectScope.Self, null, 2),
            },
            "mixed");
        var definition = Enemy("mixed_enemy", move);
        var actor = BattleFixtures.Goblin(moveId: move.Id) with
        {
            DefinitionId = definition.Id,
        };
        var state = BattleFixtures.MinimalState(
            enemies: ImmutableArray.Create(actor));
        var catalog = BattleFixtures.MinimalCatalog(
            enemies: new[] { definition });

        var intent = CombatIntentProjector.Project(state, actor, catalog);

        Assert.NotNull(intent);
        Assert.Equal(3, intent!.AttackSingle);
        Assert.Equal(4, intent.AttackRandom);
        Assert.Equal(11, intent.AttackAll);
        Assert.Equal(4, intent.AttackHits);
        Assert.Equal(1, intent.AttackSingleHits);
        Assert.Equal(1, intent.AttackRandomHits);
        Assert.Equal(2, intent.AttackAllHits);
        Assert.Equal(7, intent.Block);
        Assert.True(intent.HasBuff);
        Assert.True(intent.HasDebuff);
        Assert.True(intent.HasHeal);
    }

    [Fact]
    public void Hero_omnistrike_projects_all_queued_hits_as_all_scope()
    {
        var hero = BattleFixtures.WithOmnistrike(
            BattleFixtures.WithStrength(BattleFixtures.Hero(), 1)) with
        {
            AttackSingle = AttackPool.Empty.Add(5),
            AttackRandom = AttackPool.Empty.Add(3),
            AttackAll = AttackPool.Empty.Add(2),
        };
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero));

        var intent = CombatIntentProjector.Project(
            state, hero, BattleFixtures.MinimalCatalog());

        Assert.NotNull(intent);
        Assert.Null(intent!.AttackSingle);
        Assert.Null(intent.AttackRandom);
        Assert.Equal(13, intent.AttackAll);
        Assert.Equal(3, intent.AttackHits);
        Assert.Equal(3, intent.AttackAllHits);
    }

    [Fact]
    public void Summon_move_is_projected_from_unit_catalog()
    {
        var move = new MoveDefinition(
            "summon_barrage",
            MoveKind.Multi,
            new CardEffect[]
            {
                new("attack", EffectScope.Random, EffectSide.Enemy, 3),
                new("attack", EffectScope.All, EffectSide.Enemy, 4),
            },
            "summon_barrage");
        var unit = new UnitDefinition(
            "intent_unit", "Intent Unit", "intent_unit", 10,
            move.Id, new[] { move });
        var summon = BattleFixtures.WithStrength(
            BattleFixtures.SummonActor(
                "intent-unit-1", unit.Id, slotIndex: 1, moveId: move.Id),
            1);
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(BattleFixtures.Hero(), summon));
        var catalog = BattleFixtures.MinimalCatalog(units: new[] { unit });

        var intent = CombatIntentProjector.Project(state, summon, catalog);

        Assert.NotNull(intent);
        Assert.Equal(4, intent!.AttackRandom);
        Assert.Equal(5, intent.AttackAll);
        Assert.Equal(2, intent.AttackHits);
        Assert.Equal(1, intent.AttackRandomHits);
        Assert.Equal(1, intent.AttackAllHits);
    }

    [Fact]
    public void Unknown_or_unselected_move_has_no_projection()
    {
        var enemy = BattleFixtures.Goblin(moveId: "missing");
        var state = BattleFixtures.MinimalState(
            enemies: ImmutableArray.Create(enemy));

        Assert.Null(CombatIntentProjector.Project(
            state, enemy, BattleFixtures.MinimalCatalog()));
        Assert.Null(CombatIntentProjector.Project(
            state,
            enemy with { CurrentMoveId = null },
            BattleFixtures.MinimalCatalog()));
    }

    private static EnemyDefinition Enemy(
        string id,
        params MoveDefinition[] moves) =>
        new(
            id,
            id,
            id,
            Hp: 30,
            Pool: new EnemyPool(1, EnemyTier.Weak),
            InitialMoveId: moves.First().Id,
            Moves: moves);
}
