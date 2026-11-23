using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class PlayerAttackingResolverTests
{
    private static BattleState MakeState(
        CombatActor hero,
        params CombatActor[] enemies)
        => new(
            Turn: 1, Phase: BattlePhase.PlayerAttacking, Outcome: BattleOutcome.Pending,
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

    private static IRng Rng(params int[] ints) => new FakeRng(ints, new double[0]);

    [Fact] public void Single_attack_hits_target_enemy_only()
    {
        var hero = BattleFixtures.Hero() with { AttackSingle = AttackPool.Empty.Add(6) };
        var goblin0 = BattleFixtures.Goblin(slotIndex: 0, hp: 20);
        var goblin1 = BattleFixtures.Goblin(slotIndex: 1, hp: 20);
        var s = MakeState(hero, goblin0, goblin1);
        var (next, _) = PlayerAttackingResolver.Resolve(s, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(14, next.Enemies[0].CurrentHp);
        Assert.Equal(20, next.Enemies[1].CurrentHp);
    }

    [Fact]
    public void Required_player_death_from_reflect_stops_later_allied_attackers()
    {
        var hero = BattleFixtures.Hero(currentHp: 1, maxHp: 20) with
        {
            AttackSingle = AttackPool.Empty.Add(1),
        };
        var wisp = BattleFixtures.SummonActor(
            "wisp", "wisp", slotIndex: 1, hp: 30) with
        {
            AttackSingle = AttackPool.Empty.Add(5),
        };
        var enemy = BattleFixtures.Goblin(hp: 20) with
        {
            Statuses = BattleFixtures.Goblin().Statuses
                .SetItem("reflect", 1),
        };
        var state = MakeState(hero, enemy) with
        {
            Allies = [hero, wisp],
        };

        var (next, events) = PlayerAttackingResolver.Resolve(
            state, Rng(), BattleFixtures.MinimalCatalog());

        Assert.False(next.Allies[0].IsAlive);
        Assert.Equal(19, next.Enemies[0].CurrentHp);
        Assert.Single(events, battleEvent =>
            battleEvent.Kind == BattleEventKind.AttackFire);
    }

    [Fact] public void Single_hits_keep_the_final_selected_target_and_stop_after_it_dies()
    {
        var hero = BattleFixtures.Hero() with
        {
            AttackSingle = AttackPool.Empty.Add(5).Add(5).Add(5),
        };
        var state = MakeState(
            hero,
            BattleFixtures.Goblin(slotIndex: 0, hp: 20),
            BattleFixtures.Goblin(slotIndex: 1, hp: 5))
            with { TargetEnemyIndex = 1 };

        var (next, events) = PlayerAttackingResolver.Resolve(
            state, Rng(), BattleFixtures.MinimalCatalog());

        Assert.Equal(20, next.Enemies[0].CurrentHp);
        Assert.Equal(0, next.Enemies[1].CurrentHp);
        var hits = events.Where(e => e.Kind == BattleEventKind.AttackFire).ToList();
        Assert.Single(hits);
        Assert.Equal(0, hits[0].HitIndex);
    }

    [Fact] public void Single_retargets_when_its_fixed_target_died_before_the_actor_chain()
    {
        var hero = BattleFixtures.Hero() with
        {
            AttackSingle = AttackPool.Empty.Add(5).Add(5),
        };
        var deadTarget = BattleFixtures.Goblin(slotIndex: 0, hp: 5) with
        {
            CurrentHp = 0,
        };
        var survivor = BattleFixtures.Goblin(slotIndex: 1, hp: 20);

        var (next, events) = PlayerAttackingResolver.Resolve(
            MakeState(hero, deadTarget, survivor) with { TargetEnemyIndex = 0 },
            Rng(), BattleFixtures.MinimalCatalog());

        Assert.Equal(10, next.Enemies[1].CurrentHp);
        Assert.Equal(2, events.Count(e => e.Kind == BattleEventKind.AttackFire));
    }

    [Fact] public void All_attack_hits_every_enemy()
    {
        var hero = BattleFixtures.Hero() with { AttackAll = AttackPool.Empty.Add(4) };
        var s = MakeState(hero,
            BattleFixtures.Goblin(0, 20),
            BattleFixtures.Goblin(1, 20));
        var (next, _) = PlayerAttackingResolver.Resolve(s, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(16, next.Enemies[0].CurrentHp);
        Assert.Equal(16, next.Enemies[1].CurrentHp);
    }

    [Fact] public void All_attack_applies_each_hit_to_each_enemy_still_alive()
    {
        var hero = BattleFixtures.Hero() with
        {
            AttackAll = AttackPool.Empty.Add(5).Add(4),
        };
        var state = MakeState(
            hero,
            BattleFixtures.Goblin(slotIndex: 0, hp: 5),
            BattleFixtures.Goblin(slotIndex: 1, hp: 20));

        var (next, events) = PlayerAttackingResolver.Resolve(
            state, Rng(), BattleFixtures.MinimalCatalog());

        Assert.Equal(0, next.Enemies[0].CurrentHp);
        Assert.Equal(11, next.Enemies[1].CurrentHp);
        var hits = events.Where(e => e.Kind == BattleEventKind.AttackFire).ToList();
        Assert.Equal(3, hits.Count);
        Assert.Equal(new int?[] { 0, 0, 1 }, hits.Select(e => e.HitIndex));
        Assert.Equal(new int?[] { 5, 5, 4 }, hits.Select(e => e.Amount));
    }

    [Fact] public void Random_attack_uses_rng_to_pick_target()
    {
        var hero = BattleFixtures.Hero() with { AttackRandom = AttackPool.Empty.Add(7) };
        var s = MakeState(hero,
            BattleFixtures.Goblin(0, 20),
            BattleFixtures.Goblin(1, 20));
        var rng = Rng(1); // 2 体中 index=1 を選択
        var (next, _) = PlayerAttackingResolver.Resolve(s, rng, BattleFixtures.MinimalCatalog());
        Assert.Equal(20, next.Enemies[0].CurrentHp);
        Assert.Equal(13, next.Enemies[1].CurrentHp);
    }

    [Fact] public void Random_attack_draws_again_for_every_hit_and_excludes_dead_enemies()
    {
        var hero = BattleFixtures.Hero() with
        {
            AttackRandom = AttackPool.Empty.Add(5).Add(7).Add(3),
        };
        var state = MakeState(
            hero,
            BattleFixtures.Goblin(slotIndex: 0, hp: 5),
            BattleFixtures.Goblin(slotIndex: 1, hp: 20),
            BattleFixtures.Goblin(slotIndex: 2, hp: 20));

        // 1発目はA。A死亡後の候補[B,C]から2発目はC、3発目はB。
        var (next, events) = PlayerAttackingResolver.Resolve(
            state, Rng(0, 1, 0), BattleFixtures.MinimalCatalog());

        Assert.Equal(0, next.Enemies[0].CurrentHp);
        Assert.Equal(17, next.Enemies[1].CurrentHp);
        Assert.Equal(13, next.Enemies[2].CurrentHp);
        var hits = events.Where(e => e.Kind == BattleEventKind.AttackFire).ToList();
        Assert.Equal(
            new[] { next.Enemies[0].InstanceId, next.Enemies[2].InstanceId, next.Enemies[1].InstanceId },
            hits.Select(e => e.TargetInstanceId));
        Assert.Equal(new int?[] { 0, 1, 2 }, hits.Select(e => e.HitIndex));
    }

    [Fact] public void Random_attack_is_a_no_op_when_no_living_enemy_exists()
    {
        var hero = BattleFixtures.Hero() with
        {
            AttackRandom = AttackPool.Empty.Add(5).Add(5),
        };
        var dead = BattleFixtures.Goblin(hp: 5) with { CurrentHp = 0 };

        var (_, events) = PlayerAttackingResolver.Resolve(
            MakeState(hero, dead), Rng(), BattleFixtures.MinimalCatalog());

        Assert.DoesNotContain(events, e => e.Kind == BattleEventKind.AttackFire);
    }

    [Fact] public void Block_absorbs_damage_partially()
    {
        var hero = BattleFixtures.Hero() with { AttackSingle = AttackPool.Empty.Add(6) };
        var goblin = BattleFixtures.Goblin() with { Block = BlockPool.Empty.Add(4) };
        var s = MakeState(hero, goblin);
        var (next, _) = PlayerAttackingResolver.Resolve(s, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(18, next.Enemies[0].CurrentHp); // 20 - (6 - 4) = 18
        Assert.Equal(0, next.Enemies[0].Block.Sum); // Consume(6) from 4 → 0
    }

    [Fact] public void Block_fully_absorbs_damage()
    {
        var hero = BattleFixtures.Hero() with { AttackSingle = AttackPool.Empty.Add(3) };
        var goblin = BattleFixtures.Goblin() with { Block = BlockPool.Empty.Add(5) };
        var s = MakeState(hero, goblin);
        var (next, _) = PlayerAttackingResolver.Resolve(s, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(20, next.Enemies[0].CurrentHp);
        Assert.Equal(2, next.Enemies[0].Block.Sum); // 5 - 3 = 2
    }

    [Fact] public void Block_is_consumed_one_hit_at_a_time()
    {
        var hero = BattleFixtures.Hero() with
        {
            AttackSingle = AttackPool.Empty.Add(4).Add(4),
        };
        var goblin = BattleFixtures.Goblin(hp: 20) with { Block = BlockPool.Empty.Add(5) };

        var (next, events) = PlayerAttackingResolver.Resolve(
            MakeState(hero, goblin), Rng(), BattleFixtures.MinimalCatalog());

        Assert.Equal(17, next.Enemies[0].CurrentHp);
        var damageEvents = events.Where(e => e.Kind == BattleEventKind.DealDamage).ToList();
        Assert.Equal(new int?[] { 0, 3 }, damageEvents.Select(e => e.Amount));
        Assert.Equal(new int?[] { 4, 1 }, damageEvents.Select(e => e.BlockedAmount));
    }

    [Fact] public void Lethal_attack_emits_ActorDeath()
    {
        var hero = BattleFixtures.Hero() with { AttackSingle = AttackPool.Empty.Add(99) };
        var s = MakeState(hero, BattleFixtures.Goblin(0, 5));
        var (next, events) = PlayerAttackingResolver.Resolve(s, Rng(), BattleFixtures.MinimalCatalog());
        Assert.False(next.Enemies[0].IsAlive);
        Assert.Contains(events, e => e.Kind == BattleEventKind.ActorDeath);
    }

    [Fact] public void Pool_zero_does_not_emit_AttackFire()
    {
        var hero = BattleFixtures.Hero(); // 全 Pool 0
        var s = MakeState(hero, BattleFixtures.Goblin());
        var (_, events) = PlayerAttackingResolver.Resolve(s, Rng(), BattleFixtures.MinimalCatalog());
        Assert.DoesNotContain(events, e => e.Kind == BattleEventKind.AttackFire);
    }

    [Fact] public void Added_zero_is_still_a_hit_and_receives_strength()
    {
        var hero = BattleFixtures.WithStrength(BattleFixtures.Hero(), 2) with
        {
            AttackSingle = AttackPool.Empty.Add(0),
        };

        var (next, events) = PlayerAttackingResolver.Resolve(
            MakeState(hero, BattleFixtures.Goblin(hp: 20)),
            Rng(),
            BattleFixtures.MinimalCatalog());

        Assert.Equal(18, next.Enemies[0].CurrentHp);
        var hit = Assert.Single(events, e => e.Kind == BattleEventKind.AttackFire);
        Assert.Equal(2, hit.Amount);
        Assert.Equal(0, hit.HitIndex);
    }

    [Fact] public void Order_is_Single_then_Random_then_All()
    {
        var hero = BattleFixtures.Hero() with
        {
            AttackSingle = AttackPool.Empty.Add(1),
            AttackRandom = AttackPool.Empty.Add(1),
            AttackAll    = AttackPool.Empty.Add(1),
        };
        var s = MakeState(hero,
            BattleFixtures.Goblin(0, 20),
            BattleFixtures.Goblin(1, 20));
        var (_, events) = PlayerAttackingResolver.Resolve(s, Rng(0), BattleFixtures.MinimalCatalog());
        var fireEvents = events.Where(e => e.Kind == BattleEventKind.AttackFire).ToList();
        Assert.Equal("single", fireEvents[0].Note);
        Assert.Equal("random", fireEvents[1].Note);
        Assert.Equal("all", fireEvents[2].Note);
        Assert.Equal(new int?[] { 0, 1, 2, 2 }, fireEvents.Select(e => e.HitIndex));
    }
}
