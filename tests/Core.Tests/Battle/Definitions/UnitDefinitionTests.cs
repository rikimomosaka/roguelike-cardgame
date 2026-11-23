using System;
using System.Collections.Generic;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Definitions;

public class UnitDefinitionTests
{
    private static UnitDefinition Sample(int? lifetime = null) => new(
        Id: "wolf_summon",
        Name: "召喚狼",
        ImageId: "wolf",
        Hp: 12,
        InitialMoveId: "bite",
        Moves: new List<MoveDefinition>
        {
            new("bite", MoveKind.Attack,
                new[] { new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 4) },
                "bite"),
        },
        LifetimeTurns: lifetime);

    [Fact]
    public void Inherits_CombatActorDefinition()
    {
        Assert.IsAssignableFrom<CombatActorDefinition>(Sample());
    }

    [Fact]
    public void LifetimeTurns_defaults_to_null()
    {
        Assert.Null(Sample().LifetimeTurns);
    }

    [Fact]
    public void LifetimeTurns_accepts_positive_value()
    {
        Assert.Equal(3, Sample(lifetime: 3).LifetimeTurns);
    }

    [Fact]
    public void Hp_is_single_value()
    {
        Assert.Equal(12, Sample().Hp);
    }

    [Fact]
    public void Legacy_move_unit_is_not_card_controlled()
    {
        Assert.False(Sample().IsCardControlled);
        Assert.Empty(Sample().EffectiveDeck);
        Assert.Empty(Sample().EffectiveSkills);
        Assert.Empty(Sample().EffectiveOwnedRelicIds);
        Assert.Empty(Sample().EffectivePotions);
    }

    [Fact]
    public void Card_controlled_unit_exposes_deck_and_resources()
    {
        var unit = Sample() with
        {
            InitialMoveId = string.Empty,
            Moves = System.Array.Empty<MoveDefinition>(),
            Deck = new[] { new UnitDeckEntry("spark", Count: 2, Priority: 7) },
            InitialEnergy = 1,
            EnergyPerTurn = 1,
            DrawPerTurn = 3,
            HandLimit = 5,
            AiProfileId = "low_cost_combo",
        };

        Assert.True(unit.IsCardControlled);
        var entry = Assert.Single(unit.EffectiveDeck);
        Assert.Equal("spark", entry.CardId);
        Assert.Equal(2, entry.Count);
        Assert.Equal(7, entry.Priority);
        Assert.Equal("low_cost_combo", unit.AiProfileId);
    }

    [Fact]
    public void Card_controlled_unit_exposes_optional_combat_loadout()
    {
        var skill = new CharacterSkillDefinition(
            "spark_art", "火花術", new ResourceCost(Energy: 1, ComboGauge: 2),
            OncePerTurn: false,
            Effects: [new CardEffect("buff", EffectScope.Self, null, 1, Name: "strength")],
            Keywords: ["wild"],
            UsesPerTurn: 2);
        var unit = Sample() with
        {
            InitialMoveId = string.Empty,
            Moves = [],
            Deck = [new UnitDeckEntry("spark")],
            AiProfileId = UnitAiProfileIds.LowCostCombo,
            TraitDescription = "小さな炎。",
            Skills = [skill],
            OwnedRelicIds = ["lantern"],
            Potions = ["fire_potion"],
        };

        Assert.Equal("小さな炎。", unit.TraitDescription);
        Assert.Same(skill, Assert.Single(unit.EffectiveSkills));
        Assert.Equal("lantern", Assert.Single(unit.EffectiveOwnedRelicIds));
        Assert.Equal("fire_potion", Assert.Single(unit.EffectivePotions));
    }
}
