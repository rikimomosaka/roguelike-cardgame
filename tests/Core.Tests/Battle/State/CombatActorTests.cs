using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.State;

public class CombatActorTests
{
    private static CombatActor MakeHero(int hp = 70) =>
        new("hero1", "hero", ActorSide.Ally, 0, hp, hp,
            BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty,
            ImmutableDictionary<string, int>.Empty, null,
            RemainingLifetimeTurns: null, AssociatedSummonHeldInstanceId: null);

    [Fact] public void IsAlive_true_when_hp_positive()
    {
        var a = MakeHero(70);
        Assert.True(a.IsAlive);
    }

    [Fact] public void IsAlive_false_when_hp_zero()
    {
        var a = MakeHero(70) with { CurrentHp = 0 };
        Assert.False(a.IsAlive);
    }

    [Fact] public void IsAlive_false_when_hp_negative()
    {
        var a = MakeHero(70) with { CurrentHp = -5 };
        Assert.False(a.IsAlive);
    }

    [Fact] public void Record_equality_holds()
    {
        Assert.Equal(MakeHero(70), MakeHero(70));
    }

    [Fact] public void GetStatus_returns_zero_for_unknown()
    {
        var a = MakeHero();
        Assert.Equal(0, a.GetStatus("strength"));
    }

    [Fact] public void GetStatus_returns_amount_when_present()
    {
        var statuses = ImmutableDictionary<string, int>.Empty.Add("strength", 3);
        var a = MakeHero() with { Statuses = statuses };
        Assert.Equal(3, a.GetStatus("strength"));
    }

    [Fact] public void Record_inequality_when_statuses_differ()
    {
        var a = MakeHero();
        var b = MakeHero() with { Statuses = ImmutableDictionary<string, int>.Empty.Add("weak", 1) };
        Assert.NotEqual(a, b);
    }

    // === 10.2.D: Lifetime / AssociatedSummonHeldInstanceId ===

    [Fact] public void RemainingLifetimeTurns_null_means_permanent()
    {
        var hero = BattleFixtures.Hero();
        Assert.Null(hero.RemainingLifetimeTurns);
    }

    [Fact] public void AssociatedSummonHeldInstanceId_null_for_hero()
    {
        var hero = BattleFixtures.Hero();
        Assert.Null(hero.AssociatedSummonHeldInstanceId);
    }

    [Fact] public void Record_equality_distinguishes_lifetime_field()
    {
        var hero = BattleFixtures.Hero();
        var copy = hero with { RemainingLifetimeTurns = 3 };
        Assert.NotEqual(hero, copy);
    }

    [Fact] public void Record_equality_distinguishes_associated_summon_held()
    {
        var hero = BattleFixtures.Hero();
        var copy = hero with { AssociatedSummonHeldInstanceId = "card_x" };
        Assert.NotEqual(hero, copy);
    }
}
