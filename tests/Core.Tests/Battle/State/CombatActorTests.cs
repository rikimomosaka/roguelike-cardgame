using RoguelikeCardGame.Core.Battle.State;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.State;

public class CombatActorTests
{
    private static CombatActor MakeHero(int hp = 70) =>
        new("hero1", "hero", ActorSide.Ally, 0, hp, hp,
            BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty, null);

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
}
