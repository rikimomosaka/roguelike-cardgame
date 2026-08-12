using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Potions;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class RunPotionActionsTests
{
    private static readonly DataCatalog Catalog = EmbeddedDataLoader.LoadCatalog();

    [Fact]
    public void UseOutsideBattle_HealthPotion_HealsAndConsumesSlot()
    {
        var state = TestRunStates.FreshDefault(Catalog) with
        {
            CurrentHp = 40,
            Potions = ImmutableArray.Create("health_potion", "", ""),
        };

        var updated = RunPotionActions.UseOutsideBattle(state, 0, Catalog);

        Assert.Equal(55, updated.CurrentHp);
        Assert.Equal("", updated.Potions[0]);
    }

    [Fact]
    public void UseOutsideBattle_HealthPotion_CapsAtMaxHp()
    {
        var state = TestRunStates.FreshDefault(Catalog) with
        {
            CurrentHp = 75,
            MaxHp = 80,
            Potions = ImmutableArray.Create("health_potion", "", ""),
        };

        var updated = RunPotionActions.UseOutsideBattle(state, 0, Catalog);

        Assert.Equal(80, updated.CurrentHp);
        Assert.Equal("", updated.Potions[0]);
    }

    [Fact]
    public void UseOutsideBattle_BattleOnlyPotion_IsRejectedWithoutConsumption()
    {
        var state = TestRunStates.FreshDefault(Catalog) with
        {
            Potions = ImmutableArray.Create("fire_potion", "", ""),
        };

        Assert.Throws<InvalidOperationException>(
            () => RunPotionActions.UseOutsideBattle(state, 0, Catalog));
        Assert.Equal("fire_potion", state.Potions[0]);
    }

    [Fact]
    public void UseOutsideBattle_AllyTargetedHeal_ResolvesToSoloHero()
    {
        const string potionId = "ally_health_potion";
        var potions = new Dictionary<string, PotionDefinition>(Catalog.Potions)
        {
            [potionId] = new PotionDefinition(
                potionId,
                "Ally Health Potion",
                CardRarity.Rare,
                new[] { new CardEffect("heal", EffectScope.Single, EffectSide.Ally, 12) }),
        };
        var catalog = Catalog with { Potions = potions };
        var state = TestRunStates.FreshDefault(catalog) with
        {
            CurrentHp = 30,
            Potions = ImmutableArray.Create(potionId, "", ""),
        };

        var updated = RunPotionActions.UseOutsideBattle(state, 0, catalog);

        Assert.Equal(42, updated.CurrentHp);
        Assert.Equal("", updated.Potions[0]);
    }
}
