using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class PlayerAttackingResolverOmnistrikeTests
{
    private static BattleState State(CombatActor hero, params CombatActor[] enemies) => new(
        Turn: 1, Phase: BattlePhase.PlayerAttacking,
        Outcome: RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending,
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

    private static IRng Rng() => new FakeRng(new int[0], new double[0]);

    [Fact] public void Omnistrike_combines_pools_and_hits_all_enemies()
    {
        // Single +5 を 1 枚、All +3 を 1 枚 → combined Sum=8, AddCount=2
        var hero = BattleFixtures.WithOmnistrike(BattleFixtures.Hero(), 1) with
        {
            AttackSingle = AttackPool.Empty.Add(5),
            AttackAll    = AttackPool.Empty.Add(3),
        };
        var s = State(hero, BattleFixtures.Goblin(0, hp: 50), BattleFixtures.Goblin(1, hp: 50));
        var (next, _) = PlayerAttackingResolver.Resolve(s, Rng());
        Assert.Equal(50 - 8, next.Enemies[0].CurrentHp);
        Assert.Equal(50 - 8, next.Enemies[1].CurrentHp);
    }

    [Fact] public void Omnistrike_AddCount_combines_for_strength_calc()
    {
        // Single +5 を 2 枚 (AddCount=2)、Random +3 を 1 枚 (AddCount=1) → combined Sum=13, AddCount=3
        // strength=2 → totalAttack = 13 + 3*2 = 19
        var hero = BattleFixtures.WithOmnistrike(BattleFixtures.WithStrength(BattleFixtures.Hero(), 2), 1) with
        {
            AttackSingle = AttackPool.Empty.Add(5).Add(5),
            AttackRandom = AttackPool.Empty.Add(3),
        };
        var s = State(hero, BattleFixtures.Goblin(0, hp: 50));
        var (next, _) = PlayerAttackingResolver.Resolve(s, Rng());
        Assert.Equal(50 - 19, next.Enemies[0].CurrentHp);
    }

    [Fact] public void Omnistrike_with_empty_pools_does_not_fire()
    {
        var hero = BattleFixtures.WithOmnistrike(BattleFixtures.Hero(), 1);
        var s = State(hero, BattleFixtures.Goblin(0, hp: 50));
        var (next, evs) = PlayerAttackingResolver.Resolve(s, Rng());
        Assert.Equal(50, next.Enemies[0].CurrentHp);
        Assert.DoesNotContain(evs, e => e.Kind == BattleEventKind.AttackFire);
    }

    [Fact] public void Omnistrike_emits_attack_fire_per_enemy()
    {
        var hero = BattleFixtures.WithOmnistrike(BattleFixtures.Hero(), 1) with
        {
            AttackAll = AttackPool.Empty.Add(3),
        };
        var s = State(hero, BattleFixtures.Goblin(0), BattleFixtures.Goblin(1), BattleFixtures.Goblin(2));
        var (_, evs) = PlayerAttackingResolver.Resolve(s, Rng());
        Assert.Equal(3, evs.Count(e => e.Kind == BattleEventKind.AttackFire));
        Assert.All(evs.Where(e => e.Kind == BattleEventKind.AttackFire),
                   e => Assert.Equal("omnistrike", e.Note));
    }

    [Fact] public void Without_omnistrike_uses_single_random_all_path()
    {
        // omnistrike なし → 既存挙動。Single のみで対象 1 体に着弾
        var hero = BattleFixtures.Hero() with { AttackSingle = AttackPool.Empty.Add(5) };
        var s = State(hero, BattleFixtures.Goblin(0, hp: 20), BattleFixtures.Goblin(1, hp: 20));
        var (next, _) = PlayerAttackingResolver.Resolve(s, Rng());
        Assert.Equal(20 - 5, next.Enemies[0].CurrentHp);
        Assert.Equal(20, next.Enemies[1].CurrentHp);
    }
}
