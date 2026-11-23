using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.Projection;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
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
    public void Card_controlled_summon_projects_queued_card_attacks_without_a_move()
    {
        var unit = new UnitDefinition(
            "card_unit", "Card Unit", "card_unit", 10,
            InitialMoveId: "",
            Moves: Array.Empty<MoveDefinition>(),
            Deck: new[] { new UnitDeckEntry("dummy") },
            AiProfileId: UnitAiProfileIds.LowCostCombo);
        var summon = BattleFixtures.SummonActor(
            "card-unit-1", unit.Id, slotIndex: 1) with
        {
            AttackSingle = AttackPool.Empty.Add(4),
            AttackAll = AttackPool.Empty.Add(2),
        };
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(BattleFixtures.Hero(), summon));
        var catalog = BattleFixtures.MinimalCatalog(units: new[] { unit });

        var intent = CombatIntentProjector.Project(state, summon, catalog);

        Assert.NotNull(intent);
        Assert.Equal(4, intent!.AttackSingle);
        Assert.Equal(2, intent.AttackAll);
        Assert.Equal(2, intent.AttackHits);
        Assert.Equal(1, intent.AttackSingleHits);
        Assert.Equal(1, intent.AttackAllHits);
    }

    [Fact]
    public void Hero_intent_includes_afterimage_record_repeated_by_its_stack()
    {
        var hero = BattleFixtures.WithStatus(
            BattleFixtures.WithStrength(BattleFixtures.Hero(), 1),
            BattleStatusIds.Afterimage,
            2) with
        {
            AttackSingle = AttackPool.Empty.Add(2),
        };
        var snapshot = new PlayedCardEffectSnapshot(
            "saved-mixed",
            "saved-mixed-used",
            hero.InstanceId,
            CardType.Attack,
            OriginalCost: 2,
            PaidCost: 1,
            IsUpgraded: true,
            Keywords: ["finisher"],
            Effects:
            [
                new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 3),
                new CardEffect("attack", EffectScope.Random, EffectSide.Enemy, 4),
            ],
            PlaySequence: 7);
        var state = BattleFixtures.MinimalState(allies: [hero]) with
        {
            LastFinisherRecord = [snapshot],
        };

        var planned = AfterimageIntentProjector.Project(state, hero);
        var combined = CombatIntentProjector.Project(
            state, hero, BattleFixtures.MinimalCatalog());

        Assert.NotNull(planned);
        Assert.Equal(8, planned!.AttackSingle);
        Assert.Equal(10, planned.AttackRandom);
        Assert.Equal(2, planned.AttackSingleHits);
        Assert.Equal(2, planned.AttackRandomHits);
        Assert.Equal(4, planned.AttackHits);
        Assert.NotNull(combined);
        Assert.Equal(11, combined!.AttackSingle);
        Assert.Equal(10, combined.AttackRandom);
        Assert.Equal(5, combined.AttackHits);
    }

    [Fact]
    public void Afterimage_dry_run_honors_conditions_amount_source_and_self_changes()
    {
        var hero = BattleFixtures.WithStatus(
            BattleFixtures.WithStatus(
                BattleFixtures.Hero() with
                {
                    Block = BlockPool.Empty.Add(4),
                },
                BattleStatusIds.Afterimage,
                1),
            BattleStatusIds.Disruption,
            2);
        hero = BattleFixtures.WithStatus(hero, BattleStatusIds.Omnistrike, 1);
        var state = BattleFixtures.MinimalState(allies: [hero]) with
        {
            ComboCount = 2,
            MaxComboThisTurn = 4,
            LastFinisherRecord =
            [
                new PlayedCardEffectSnapshot(
                    "saved-sequence",
                    "saved-sequence-used",
                    hero.InstanceId,
                    CardType.Attack,
                    OriginalCost: 3,
                    PaidCost: 2,
                    IsUpgraded: false,
                    Keywords: [],
                    Effects:
                    [
                        new CardEffect("block", EffectScope.Self, null, 3),
                        new CardEffect(
                            "attack", EffectScope.Single, EffectSide.Enemy, 0,
                            AmountSource: "selfBlock", MaxComboMin: 4),
                        new CardEffect(
                            "buff", EffectScope.Self, null, 2, Name: "strength"),
                        new CardEffect(
                            "attack", EffectScope.Random, EffectSide.Enemy, 5,
                            ComboMin: 2),
                        new CardEffect(
                            "attack", EffectScope.All, EffectSide.Enemy, 99,
                            MaxComboMin: 5),
                    ],
                    PlaySequence: 1),
            ],
        };

        var intent = AfterimageIntentProjector.Project(state, hero);

        Assert.NotNull(intent);
        // Both attacks resolve after the self Strength +2, while selfBlock is
        // evaluated when its effect is queued: (4 + 3) + 2 and 5 + 2.
        // Omnistrike takes precedence over Disruption.
        Assert.Equal(16, intent!.AttackAll);
        Assert.Equal(2, intent.AttackAllHits);
        Assert.Equal(2, intent.AttackHits);
        Assert.Null(intent.AttackSingle);
        Assert.Null(intent.AttackRandom);
        Assert.Equal(0, hero.GetStatus(BattleStatusIds.Strength));
        Assert.Equal(4, hero.Block.Sum);
    }

    [Fact]
    public void Afterimage_dry_run_matches_runtime_after_deterministic_hp_energy_and_resistance_changes()
    {
        var hero = BattleFixtures.WithStatus(
            BattleFixtures.WithStatus(
                BattleFixtures.Hero(currentHp: 40, maxHp: 50),
                BattleStatusIds.Afterimage,
                1),
            BattleStatusIds.Resistance,
            1);
        var enemy = BattleFixtures.Goblin(hp: 500);
        var state = BattleFixtures.MinimalState(
            allies: [hero],
            enemies: [enemy],
            phase: BattlePhase.PlayerAttacking,
            energy: 2) with
        {
            LastFinisherRecord =
            [
                new PlayedCardEffectSnapshot(
                    "saved-runtime-sequence",
                    "saved-runtime-sequence-used",
                    hero.InstanceId,
                    CardType.Attack,
                    OriginalCost: 3,
                    PaidCost: 3,
                    IsUpgraded: false,
                    Keywords: [],
                    Effects:
                    [
                        new CardEffect(
                            "selfDamage", EffectScope.Self, null, 7),
                        new CardEffect(
                            "attack", EffectScope.Single, EffectSide.Enemy, 0,
                            AmountSource: "selfHpLost"),
                        new CardEffect(
                            "heal", EffectScope.Self, null, 5),
                        new CardEffect(
                            "attack", EffectScope.Single, EffectSide.Enemy, 0,
                            AmountSource: "selfHp"),
                        new CardEffect(
                            "gainEnergy", EffectScope.Self, null, 4),
                        new CardEffect(
                            "attack", EffectScope.Single, EffectSide.Enemy, 0,
                            AmountSource: "energy"),
                        new CardEffect(
                            "debuff", EffectScope.Self, null, 1,
                            Name: BattleStatusIds.Weak),
                        new CardEffect(
                            "debuff", EffectScope.Self, null, -2,
                            Name: BattleStatusIds.Strength),
                        new CardEffect(
                            "attack", EffectScope.Single, EffectSide.Enemy, 10),
                    ],
                    PlaySequence: 1),
            ],
        };

        var planned = AfterimageIntentProjector.Project(state, hero);
        var (resolved, events, paused) =
            BattleEngine.ResolveAfterimageAtActorEnd(
                state,
                hero.InstanceId,
                new FakeRng([], []),
                BattleFixtures.MinimalCatalog(),
                orderStart: 0);
        var actualEnemyHits = events
            .Where(battleEvent => battleEvent.Kind == BattleEventKind.DealDamage
                && battleEvent.TargetInstanceId == enemy.InstanceId)
            .ToArray();

        Assert.False(paused);
        Assert.NotNull(planned);
        Assert.Equal(actualEnemyHits.Length, planned!.AttackSingleHits);
        Assert.Equal(
            actualEnemyHits.Sum(battleEvent => battleEvent.Amount),
            planned.AttackSingle);
        Assert.Equal(63, planned.AttackSingle);
        Assert.Equal(38, resolved.Allies[0].CurrentHp);
        Assert.Equal(6, resolved.Energy);
        Assert.Equal(0, resolved.Allies[0].GetStatus(BattleStatusIds.Resistance));
        Assert.Equal(0, resolved.Allies[0].GetStatus(BattleStatusIds.Weak));
        Assert.Equal(-2, resolved.Allies[0].GetStatus(BattleStatusIds.Strength));
    }

    [Fact]
    public void Afterimage_projects_fixed_attacks_even_when_saved_card_draws()
    {
        var hero = BattleFixtures.WithStatus(
            BattleFixtures.Hero(),
            BattleStatusIds.Afterimage,
            1);
        var state = BattleFixtures.MinimalState(allies: [hero]) with
        {
            LastFinisherRecord =
            [
                new PlayedCardEffectSnapshot(
                    "saved-draw",
                    "saved-draw-used",
                    hero.InstanceId,
                    CardType.Attack,
                    OriginalCost: 1,
                    PaidCost: 1,
                    IsUpgraded: false,
                    Keywords: [],
                    Effects:
                    [
                        new CardEffect(
                            "attack", EffectScope.Single, EffectSide.Enemy, 7),
                        new CardEffect(
                            "draw", EffectScope.Self, null, 1),
                        new CardEffect(
                            "attack", EffectScope.Single, EffectSide.Enemy, 3),
                    ],
                    PlaySequence: 1),
            ],
        };

        var result = AfterimageIntentProjector.Analyze(state, hero);

        Assert.Equal(
            AfterimageIntentProjectionAvailability.Available,
            result.Availability);
        Assert.Equal(10, result.Intent!.AttackSingle);
        Assert.Equal(2, result.Intent.AttackSingleHits);
    }

    [Fact]
    public void Afterimage_dry_run_does_not_guess_choice_driven_pile_amounts()
    {
        var hero = BattleFixtures.WithStatus(
            BattleFixtures.Hero(),
            BattleStatusIds.Afterimage,
            1);
        var state = BattleFixtures.MinimalState(
            allies: [hero],
            hand:
            [
                BattleFixtures.MakeBattleCard("first", "first-1"),
                BattleFixtures.MakeBattleCard("second", "second-1"),
            ]) with
        {
            LastFinisherRecord =
            [
                new PlayedCardEffectSnapshot(
                    "saved-choice",
                    "saved-choice-used",
                    hero.InstanceId,
                    CardType.Attack,
                    OriginalCost: 2,
                    PaidCost: 2,
                    IsUpgraded: false,
                    Keywords: [],
                    Effects:
                    [
                        new CardEffect(
                            "discard", EffectScope.Self, null, 1,
                            Pile: "hand", Select: "choose"),
                        new CardEffect(
                            "attack", EffectScope.Single, EffectSide.Enemy, 0,
                            AmountSource: "handCount"),
                    ],
                    PlaySequence: 1),
            ],
        };

        var result = AfterimageIntentProjector.Analyze(state, hero);

        Assert.Equal(
            AfterimageIntentProjectionAvailability.Unavailable,
            result.Availability);
        Assert.Null(result.Intent);
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
