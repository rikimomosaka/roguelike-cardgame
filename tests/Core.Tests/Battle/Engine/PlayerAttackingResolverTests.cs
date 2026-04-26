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
            EncounterId: "enc_test");

    private static IRng Rng(params int[] ints) => new FakeRng(ints, new double[0]);

    [Fact] public void Single_attack_hits_target_enemy_only()
    {
        var hero = BattleFixtures.Hero() with { AttackSingle = AttackPool.Empty.Add(6) };
        var goblin0 = BattleFixtures.Goblin(slotIndex: 0, hp: 20);
        var goblin1 = BattleFixtures.Goblin(slotIndex: 1, hp: 20);
        var s = MakeState(hero, goblin0, goblin1);
        var (next, _) = PlayerAttackingResolver.Resolve(s, Rng());
        Assert.Equal(14, next.Enemies[0].CurrentHp);
        Assert.Equal(20, next.Enemies[1].CurrentHp);
    }

    [Fact] public void All_attack_hits_every_enemy()
    {
        var hero = BattleFixtures.Hero() with { AttackAll = AttackPool.Empty.Add(4) };
        var s = MakeState(hero,
            BattleFixtures.Goblin(0, 20),
            BattleFixtures.Goblin(1, 20));
        var (next, _) = PlayerAttackingResolver.Resolve(s, Rng());
        Assert.Equal(16, next.Enemies[0].CurrentHp);
        Assert.Equal(16, next.Enemies[1].CurrentHp);
    }

    [Fact] public void Random_attack_uses_rng_to_pick_target()
    {
        var hero = BattleFixtures.Hero() with { AttackRandom = AttackPool.Empty.Add(7) };
        var s = MakeState(hero,
            BattleFixtures.Goblin(0, 20),
            BattleFixtures.Goblin(1, 20));
        var rng = Rng(1); // 2 体中 index=1 を選択
        var (next, _) = PlayerAttackingResolver.Resolve(s, rng);
        Assert.Equal(20, next.Enemies[0].CurrentHp);
        Assert.Equal(13, next.Enemies[1].CurrentHp);
    }

    [Fact] public void Block_absorbs_damage_partially()
    {
        var hero = BattleFixtures.Hero() with { AttackSingle = AttackPool.Empty.Add(6) };
        var goblin = BattleFixtures.Goblin() with { Block = BlockPool.Empty.Add(4) };
        var s = MakeState(hero, goblin);
        var (next, _) = PlayerAttackingResolver.Resolve(s, Rng());
        Assert.Equal(18, next.Enemies[0].CurrentHp); // 20 - (6 - 4) = 18
        Assert.Equal(0, next.Enemies[0].Block.Sum); // Consume(6) from 4 → 0
    }

    [Fact] public void Block_fully_absorbs_damage()
    {
        var hero = BattleFixtures.Hero() with { AttackSingle = AttackPool.Empty.Add(3) };
        var goblin = BattleFixtures.Goblin() with { Block = BlockPool.Empty.Add(5) };
        var s = MakeState(hero, goblin);
        var (next, _) = PlayerAttackingResolver.Resolve(s, Rng());
        Assert.Equal(20, next.Enemies[0].CurrentHp);
        Assert.Equal(2, next.Enemies[0].Block.Sum); // 5 - 3 = 2
    }

    [Fact] public void Lethal_attack_emits_ActorDeath()
    {
        var hero = BattleFixtures.Hero() with { AttackSingle = AttackPool.Empty.Add(99) };
        var s = MakeState(hero, BattleFixtures.Goblin(0, 5));
        var (next, events) = PlayerAttackingResolver.Resolve(s, Rng());
        Assert.False(next.Enemies[0].IsAlive);
        Assert.Contains(events, e => e.Kind == BattleEventKind.ActorDeath);
    }

    [Fact] public void Pool_zero_does_not_emit_AttackFire()
    {
        var hero = BattleFixtures.Hero(); // 全 Pool 0
        var s = MakeState(hero, BattleFixtures.Goblin());
        var (_, events) = PlayerAttackingResolver.Resolve(s, Rng());
        Assert.DoesNotContain(events, e => e.Kind == BattleEventKind.AttackFire);
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
        var (_, events) = PlayerAttackingResolver.Resolve(s, Rng(0));
        var fireEvents = events.Where(e => e.Kind == BattleEventKind.AttackFire).ToList();
        Assert.Equal("single", fireEvents[0].Note);
        Assert.Equal("random", fireEvents[1].Note);
        Assert.Equal("all", fireEvents[2].Note);
    }
}
