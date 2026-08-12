using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class ProvisionalSoloHeroOwnerResolverTests
{
    [Fact]
    public void Resolve_returns_living_hero_with_lowest_slot_and_its_array_index()
    {
        var highSlot = BattleFixtures.Hero(slotIndex: 8) with
        {
            InstanceId = "hero-high",
        };
        var deadLowestSlot = BattleFixtures.Hero(slotIndex: 0) with
        {
            InstanceId = "hero-dead",
            CurrentHp = 0,
        };
        var lowLivingSlot = BattleFixtures.Hero(slotIndex: 2) with
        {
            InstanceId = "hero-low-living",
        };
        var state = BattleFixtures.MinimalState(allies: ImmutableArray.Create(
            highSlot, deadLowestSlot, lowLivingSlot));

        var owner = Assert.IsType<ProvisionalSoloHeroOwner>(
            ProvisionalSoloHeroOwnerResolver.Resolve(state));

        Assert.Equal("hero-low-living", owner.Hero.InstanceId);
        Assert.Equal(2, owner.AllyIndex);
    }

    [Fact]
    public void Resolve_preserves_array_order_when_living_heroes_share_the_same_slot()
    {
        var first = BattleFixtures.Hero(slotIndex: 3) with { InstanceId = "hero-first" };
        var second = BattleFixtures.Hero(slotIndex: 3) with { InstanceId = "hero-second" };
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(first, second));

        var owner = Assert.IsType<ProvisionalSoloHeroOwner>(
            ProvisionalSoloHeroOwnerResolver.Resolve(state));

        Assert.Equal("hero-first", owner.Hero.InstanceId);
        Assert.Equal(0, owner.AllyIndex);
    }

    [Fact]
    public void Resolve_returns_null_when_no_living_hero_exists()
    {
        var deadHero = BattleFixtures.Hero() with { CurrentHp = 0 };
        var summon = BattleFixtures.SummonActor("summon", "wisp", slotIndex: 1);
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(deadHero, summon));

        Assert.Null(ProvisionalSoloHeroOwnerResolver.Resolve(state));
    }
}
