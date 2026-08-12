using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class PlayerAttackingResolverStatusTests
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
        OwnedRelicIds: ImmutableArray<string>.Empty,
        Potions: ImmutableArray<string>.Empty,
        EncounterId: "enc_test");

    private static IRng Rng() => new FakeRng(new int[0], new double[0]);

    [Fact] public void Strength_boosts_single_attack()
    {
        // Sum=8, AddCount=2 (= 4 + 4 加算した結果), strength=3 → 8 + 2*3 = 14
        var hero = BattleFixtures.WithStrength(BattleFixtures.Hero(), 3) with
        {
            AttackSingle = AttackPool.Empty.Add(4).Add(4),
        };
        var s = State(hero, BattleFixtures.Goblin(hp: 30));
        var (next, _) = PlayerAttackingResolver.Resolve(s, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(30 - 14, next.Enemies[0].CurrentHp);
    }

    [Fact] public void Weak_reduces_attack()
    {
        // Sum=10, AddCount=1, weak=1 → floor(10*0.75) = 7
        var hero = BattleFixtures.WithWeak(BattleFixtures.Hero(), 1) with
        {
            AttackSingle = AttackPool.Empty.Add(10),
        };
        var s = State(hero, BattleFixtures.Goblin(hp: 30));
        var (next, _) = PlayerAttackingResolver.Resolve(s, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(30 - 7, next.Enemies[0].CurrentHp);
    }

    [Fact] public void Weak_rounds_each_hit_before_damage_is_summed()
    {
        var hero = BattleFixtures.WithWeak(BattleFixtures.Hero(), 1) with
        {
            AttackSingle = AttackPool.Empty.Add(5).Add(5).Add(5),
        };

        var (next, _) = PlayerAttackingResolver.Resolve(
            State(hero, BattleFixtures.Goblin(hp: 30)),
            Rng(),
            BattleFixtures.MinimalCatalog());

        // floor(5*0.75)=3 を3回。合計15へ掛けた11にはならない。
        Assert.Equal(21, next.Enemies[0].CurrentHp);
    }

    [Fact] public void Vulnerable_amplifies_damage_after_block()
    {
        // attack=10, target.Block=Sum=4, vuln=1 → rawDamage=6 → vuln 9
        var hero = BattleFixtures.Hero() with { AttackSingle = AttackPool.Empty.Add(10) };
        var goblin = BattleFixtures.WithVulnerable(BattleFixtures.Goblin(hp: 30), 1) with
        {
            Block = BlockPool.Empty.Add(4),
        };
        var s = State(hero, goblin);
        var (next, _) = PlayerAttackingResolver.Resolve(s, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(30 - 9, next.Enemies[0].CurrentHp);
    }

    [Fact] public void Vulnerable_rounds_each_hit_after_its_block_calculation()
    {
        var hero = BattleFixtures.Hero() with
        {
            AttackSingle = AttackPool.Empty.Add(5).Add(5).Add(5),
        };
        var goblin = BattleFixtures.WithVulnerable(BattleFixtures.Goblin(hp: 30), 1);

        var (next, _) = PlayerAttackingResolver.Resolve(
            State(hero, goblin), Rng(), BattleFixtures.MinimalCatalog());

        // floor(5*1.5)=7 を3回。合計15へ掛けた22にはならない。
        Assert.Equal(9, next.Enemies[0].CurrentHp);
    }

    [Fact] public void Dexterity_boosts_target_block_against_attack()
    {
        // attack=10, target.Block=Sum=2 AddCount=1, dex=5 → Display=7, absorbed=7, rawDamage=3
        var hero = BattleFixtures.Hero() with { AttackSingle = AttackPool.Empty.Add(10) };
        var goblin = BattleFixtures.WithDexterity(BattleFixtures.Goblin(hp: 30), 5) with
        {
            Block = BlockPool.Empty.Add(2),
        };
        var s = State(hero, goblin);
        var (next, _) = PlayerAttackingResolver.Resolve(s, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(30 - 3, next.Enemies[0].CurrentHp);
    }

    [Fact] public void All_corrections_combined_via_resolver()
    {
        // 各hit: floor((4+3)*0.75)=5。
        // 1発目はBlock 2を消費して floor(3*1.5)=4、2発目は floor(5*1.5)=7。
        var hero = BattleFixtures.WithWeak(BattleFixtures.WithStrength(BattleFixtures.Hero(), 3), 1) with
        {
            AttackSingle = AttackPool.Empty.Add(4).Add(4),
        };
        var goblin = BattleFixtures.WithVulnerable(BattleFixtures.Goblin(hp: 30), 1) with
        {
            Block = BlockPool.Empty.Add(2),
        };
        var s = State(hero, goblin);
        var (next, _) = PlayerAttackingResolver.Resolve(s, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(30 - 11, next.Enemies[0].CurrentHp);
    }
}
