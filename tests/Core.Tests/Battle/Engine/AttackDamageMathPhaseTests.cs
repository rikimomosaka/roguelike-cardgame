using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Projection;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

/// <summary>動の 1.5 倍（攻撃を当てる瞬間の位相で決まる。予告も同じ計算）。</summary>
public sealed class AttackDamageMathPhaseTests
{
    private static IRng Rng() => new FakeRng(new int[64], new double[16]);

    [Fact]
    public void Motion_MultipliesBeforeWeak() =>
        // floor(floor((7+1) × 1.5) × 3/4) = floor(12 × 0.75) = 9
        Assert.Equal(9, AttackDamageMath.AdjustForAttacker(7, strength: 1, weak: 1, vigor: 0, motion: true));

    [Fact]
    public void Motion_StacksWithVigor() =>
        // floor(floor(7 × 1.5) × 1.5) = floor(10 × 1.5) = 15
        Assert.Equal(15, AttackDamageMath.AdjustForAttacker(7, strength: 0, weak: 0, vigor: 1, motion: true));

    [Fact]
    public void NoMotion_Unchanged() =>
        Assert.Equal(7, AttackDamageMath.AdjustForAttacker(7, 0, 0));

    private static BattleState HeroWithQueuedAttack(ActorPhase phase) =>
        BattleFixtures.MinimalState(allies: ImmutableArray.Create(BattleFixtures.Hero() with
        {
            Phase = phase,
            AttackSingle = AttackPool.Empty.Add(7),
        }));

    [Theory]
    [InlineData(ActorPhase.Motion, 10)]
    [InlineData(ActorPhase.None, 7)]
    [InlineData(ActorPhase.Calm, 7)]
    public void PlayerAttacking_UsesPhaseAtResolution(ActorPhase phase, int expectedDamage)
    {
        var state = HeroWithQueuedAttack(phase) with { Phase = BattlePhase.PlayerAttacking };

        var (after, _) = PlayerAttackingResolver.Resolve(state, Rng(), BattleFixtures.MinimalCatalog());

        Assert.Equal(20 - expectedDamage, after.Enemies[0].CurrentHp);
    }

    [Theory]
    [InlineData(ActorPhase.Motion, 10)]
    [InlineData(ActorPhase.None, 7)]
    public void Intent_ShowsMotionDamage(ActorPhase phase, int expected)
    {
        var state = HeroWithQueuedAttack(phase);

        var intent = CombatIntentProjector.Project(state, state.Allies[0], BattleFixtures.MinimalCatalog());

        Assert.Equal(expected, intent!.AttackSingle);
    }

    [Fact]
    public void Enemy_Motion_DealsMoreDamage()
    {
        var state = BattleFixtures.MinimalState(enemies: ImmutableArray.Create(
            BattleFixtures.Goblin() with { Phase = ActorPhase.Motion }))
            with { Phase = BattlePhase.EnemyAttacking };

        var (after, _) = EnemyAttackingResolver.Resolve(state, Rng(), BattleFixtures.MinimalCatalog());

        // GoblinDef の swing は 5 アタック → 動で floor(7.5) = 7
        Assert.Equal(70 - 7, after.Allies[0].CurrentHp);
    }
}
