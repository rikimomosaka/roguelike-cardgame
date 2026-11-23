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
        SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
        PowerCards: ImmutableArray<BattleCardInstance>.Empty,
        ComboCount: 0,
        LastPlayedOrigCost: null,
        NextCardComboFreePass: false,
        OwnedRelicIds: ImmutableArray<string>.Empty,
        Potions: ImmutableArray<string>.Empty,
        EncounterId: "enc_test");

    private static BattleState MakePartyState(
        CombatActor[] allies, params CombatActor[] enemies) =>
        MakeState(allies[0], enemies) with { Allies = allies.ToImmutableArray() };

    private static IRng Rng(params int[] values) =>
        new FakeRng(values.Length == 0 ? new int[10] : values, new double[0]);

    private static EnemyDefinition EnemyWithAttackEffects(
        string id, params CardEffect[] effects) =>
        new(id, id, $"img_{id}", 30, new EnemyPool(1, EnemyTier.Weak), "attack",
            new[] { new MoveDefinition("attack", MoveKind.Attack, effects, "attack") });

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

    [Fact]
    public void Enemy_attack_emits_gain_attack_on_the_attacker_before_it_fires()
    {
        var hero = BattleFixtures.Hero();
        var goblin = BattleFixtures.Goblin();
        var state = MakeState(hero, goblin);
        var catalog = BattleFixtures.MinimalCatalog(
            enemies: new[] { BattleFixtures.GoblinDef(hp: 20, attack: 5) });

        var (_, events) = EnemyAttackingResolver.Resolve(state, Rng(), catalog);

        var ordered = events.ToList();
        var gainIndex = ordered.FindIndex(
            battleEvent => battleEvent.Kind == BattleEventKind.GainAttack);
        var fireIndex = ordered.FindIndex(
            battleEvent => battleEvent.Kind == BattleEventKind.AttackFire);
        Assert.InRange(gainIndex, 0, int.MaxValue);
        Assert.True(gainIndex < fireIndex);
        var gain = ordered[gainIndex];
        Assert.Equal(goblin.InstanceId, gain.CasterInstanceId);
        Assert.Equal(goblin.InstanceId, gain.TargetInstanceId);
        Assert.Equal(5, gain.Amount);
        Assert.Equal("all", gain.Note);
        Assert.Equal(CombatSourceKind.EnemyMove, gain.Source?.Kind);
    }

    [Fact]
    public void Required_player_death_stops_later_enemies_before_they_attack_tokens()
    {
        var hero = BattleFixtures.Hero(currentHp: 1, maxHp: 20);
        var wisp = BattleFixtures.SummonActor(
            "wisp", "wisp", slotIndex: 1, hp: 30);
        var enemy0 = BattleFixtures.Goblin(slotIndex: 0) with
        {
            InstanceId = "enemy-0",
            CurrentMoveId = "attack",
        };
        var enemy1 = BattleFixtures.Goblin(slotIndex: 1) with
        {
            InstanceId = "enemy-1",
            CurrentMoveId = "attack",
        };
        var definition = EnemyWithAttackEffects(
            "goblin",
            new CardEffect(
                "attack", EffectScope.Single, EffectSide.Enemy, 5));

        var (next, events) = EnemyAttackingResolver.Resolve(
            MakePartyState([hero, wisp], enemy0, enemy1),
            Rng(),
            BattleFixtures.MinimalCatalog(enemies: [definition]));

        Assert.False(next.Allies.Single(actor => actor.InstanceId == hero.InstanceId).IsAlive);
        Assert.Equal(30, next.Allies.Single(actor => actor.InstanceId == wisp.InstanceId).CurrentHp);
        Assert.Single(events, battleEvent =>
            battleEvent.Kind == BattleEventKind.AttackFire);
    }

    [Fact]
    public void Enemy_attack_scope_single_hits_only_lowest_living_ally_slot()
    {
        var hero = BattleFixtures.Hero(70, slotIndex: 1);
        var summon = BattleFixtures.SummonActor("summon_inst", "test_summon", 0, hp: 10);
        var enemy = BattleFixtures.Goblin() with { CurrentMoveId = "attack" };
        var def = EnemyWithAttackEffects("goblin",
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5));

        var (next, _) = EnemyAttackingResolver.Resolve(
            MakePartyState(new[] { hero, summon }, enemy), Rng(),
            BattleFixtures.MinimalCatalog(enemies: new[] { def }));

        Assert.Equal(70, next.Allies.Single(a => a.InstanceId == hero.InstanceId).CurrentHp);
        Assert.Equal(5, next.Allies.Single(a => a.InstanceId == summon.InstanceId).CurrentHp);
    }

    [Fact]
    public void Enemy_single_target_stays_fixed_when_an_earlier_hit_kills_it()
    {
        var hero = BattleFixtures.Hero(70, slotIndex: 1);
        var summon = BattleFixtures.SummonActor("summon_inst", "test_summon", 0, hp: 3);
        var enemy = BattleFixtures.Goblin() with { CurrentMoveId = "attack" };
        var def = EnemyWithAttackEffects("goblin",
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 3),
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 9));

        var (next, events) = EnemyAttackingResolver.Resolve(
            MakePartyState(new[] { hero, summon }, enemy), Rng(),
            BattleFixtures.MinimalCatalog(enemies: new[] { def }));

        Assert.Equal(0, next.Allies.Single(a => a.InstanceId == summon.InstanceId).CurrentHp);
        Assert.Equal(70, next.Allies.Single(a => a.InstanceId == hero.InstanceId).CurrentHp);
        Assert.Single(events, e => e.Kind == BattleEventKind.DealDamage);
    }

    [Fact]
    public void Enemy_attack_scope_random_draws_again_for_each_attack_effect()
    {
        var hero = BattleFixtures.Hero(70, slotIndex: 0);
        var summon = BattleFixtures.SummonActor("summon_inst", "test_summon", 1, hp: 10);
        var enemy = BattleFixtures.Goblin() with { CurrentMoveId = "attack" };
        var def = EnemyWithAttackEffects("goblin",
            new CardEffect("attack", EffectScope.Random, EffectSide.Enemy, 3),
            new CardEffect("attack", EffectScope.Random, EffectSide.Enemy, 4));

        var (next, events) = EnemyAttackingResolver.Resolve(
            MakePartyState(new[] { hero, summon }, enemy), Rng(1, 0),
            BattleFixtures.MinimalCatalog(enemies: new[] { def }));

        Assert.Equal(66, next.Allies.Single(a => a.InstanceId == hero.InstanceId).CurrentHp);
        Assert.Equal(7, next.Allies.Single(a => a.InstanceId == summon.InstanceId).CurrentHp);
        Assert.Equal(
            new int?[] { 0, 1 },
            events.Where(e => e.Kind == BattleEventKind.DealDamage).Select(e => e.HitIndex));
        Assert.All(
            events.Where(e => e.Kind == BattleEventKind.DealDamage),
            e => Assert.Equal("random", e.Note));
    }

    [Fact]
    public void Enemy_random_excludes_an_ally_killed_by_the_previous_hit()
    {
        var hero = BattleFixtures.Hero(70, slotIndex: 0);
        var summon = BattleFixtures.SummonActor("summon_inst", "test_summon", 1, hp: 3);
        var enemy = BattleFixtures.Goblin() with { CurrentMoveId = "attack" };
        var def = EnemyWithAttackEffects("goblin",
            new CardEffect("attack", EffectScope.Random, EffectSide.Enemy, 3),
            new CardEffect("attack", EffectScope.Random, EffectSide.Enemy, 4));

        var (next, _) = EnemyAttackingResolver.Resolve(
            MakePartyState(new[] { hero, summon }, enemy), Rng(1, 0),
            BattleFixtures.MinimalCatalog(enemies: new[] { def }));

        Assert.Equal(66, next.Allies.Single(a => a.InstanceId == hero.InstanceId).CurrentHp);
        Assert.Equal(0, next.Allies.Single(a => a.InstanceId == summon.InstanceId).CurrentHp);
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
            BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty,
            ImmutableDictionary<string, int>.Empty, "double",
            RemainingLifetimeTurns: null, AssociatedSummonHeldInstanceId: null);   // 10.2.D
        var s = MakeState(hero, enemy);
        var cat = BattleFixtures.MinimalCatalog(enemies: new[] { twoHits });
        var (next, events) = EnemyAttackingResolver.Resolve(s, Rng(), cat);
        // 2 回 × 3 ダメージ = 6
        Assert.Equal(64, next.Allies[0].CurrentHp);
        var dealCount = events.Count(e => e.Kind == BattleEventKind.DealDamage);
        Assert.Equal(2, dealCount);
        Assert.Equal(
            new int?[] { 0, 1 },
            events.Where(e => e.Kind == BattleEventKind.DealDamage).Select(e => e.HitIndex));
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
            BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty,
            ImmutableDictionary<string, int>.Empty, "guard",
            RemainingLifetimeTurns: null, AssociatedSummonHeldInstanceId: null);   // 10.2.D
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
            BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty,
            ImmutableDictionary<string, int>.Empty, "a",
            RemainingLifetimeTurns: null, AssociatedSummonHeldInstanceId: null);   // 10.2.D
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

    [Fact]
    public void Multi_ally_attack_hits_all_allies_correctly()
    {
        // Regression test for Phase 10.2.A latent bug:
        // The original implementation used state.Allies.IndexOf(ally) on snapshot
        // references. With multiple allies and per-effect state mutations, the
        // snapshot becomes stale. The fix uses InstanceId-based lookup.
        var hero = BattleFixtures.Hero();
        var summon = new CombatActor("summon_inst", "test_summon", ActorSide.Ally,
            SlotIndex: 1, CurrentHp: 10, MaxHp: 10,
            BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty,
            ImmutableDictionary<string, int>.Empty, null,
            RemainingLifetimeTurns: null, AssociatedSummonHeldInstanceId: null);   // 10.2.D
        var s = new BattleState(
            Turn: 1, Phase: BattlePhase.EnemyAttacking, Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(hero, summon),
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: 0, EnergyMax: 3,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0,
            LastPlayedOrigCost: null,
            NextCardComboFreePass: false,
            OwnedRelicIds: ImmutableArray<string>.Empty,
            Potions: ImmutableArray<string>.Empty,
            EncounterId: "enc_test");
        var cat = BattleFixtures.MinimalCatalog(
            enemies: new[] { BattleFixtures.GoblinDef(hp: 20, attack: 5) });
        var (next, events) = EnemyAttackingResolver.Resolve(s, Rng(), cat);
        // Both allies should take 5 damage
        Assert.Equal(65, next.Allies[0].CurrentHp); // hero: 70 - 5
        Assert.Equal(5, next.Allies[1].CurrentHp);  // summon: 10 - 5
        Assert.All(
            events.Where(e => e.Kind == BattleEventKind.DealDamage),
            e => Assert.Equal(0, e.HitIndex));
    }
}
