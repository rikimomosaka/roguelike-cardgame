using System.Collections.Generic;
using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Cards;

public class CardDefinitionTests
{
    [Fact]
    public void Strike_BasicShape()
    {
        var def = new CardDefinition(
            Id: "strike",
            Name: "ストライク",
            DisplayName: null,
            Rarity: CardRarity.Common,
            CardType: CardType.Attack,
            Cost: 1,
            UpgradedCost: null,
            Effects: new List<CardEffect> { new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 6) },
            UpgradedEffects: new List<CardEffect> { new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 9) },
            Keywords: null);

        Assert.Equal("strike", def.Id);
        Assert.Null(def.DisplayName);
        Assert.Equal(1, def.Cost);
        Assert.Single(def.Effects);
        Assert.NotNull(def.UpgradedEffects);
    }

    [Fact]
    public void UnplayableCard_HasNullCost()
    {
        var def = new CardDefinition(
            Id: "curse_doubt",
            Name: "Doubt",
            DisplayName: null,
            Rarity: CardRarity.Common,
            CardType: CardType.Skill,
            Cost: null,
            UpgradedCost: null,
            Effects: new List<CardEffect>(),
            UpgradedEffects: null,
            Keywords: null);

        Assert.Null(def.Cost);
        Assert.Null(def.UpgradedEffects);
    }

    [Fact]
    public void DisplayName_CanOverrideName()
    {
        var def = new CardDefinition(
            Id: "strike_promo_anniversary",
            Name: "ストライク",
            DisplayName: "ストライク(周年記念)",
            Rarity: CardRarity.Promo,
            CardType: CardType.Attack,
            Cost: 1,
            UpgradedCost: null,
            Effects: new List<CardEffect> { new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 6) },
            UpgradedEffects: null,
            Keywords: null);

        Assert.Equal("ストライク(周年記念)", def.DisplayName);
        Assert.Equal("ストライク", def.Name);
    }

    [Fact]
    public void IsUpgradable_false_when_neither_upgradedCost_nor_upgradedEffects()
    {
        var def = new CardDefinition(
            "x", "x", null, CardRarity.Common, CardType.Skill,
            Cost: 1, UpgradedCost: null,
            Effects: System.Array.Empty<CardEffect>(),
            UpgradedEffects: null,
            Keywords: null);
        Assert.False(def.IsUpgradable);
    }

    [Fact]
    public void IsUpgradable_true_when_upgradedCost_only()
    {
        var def = new CardDefinition(
            "x", "x", null, CardRarity.Common, CardType.Skill,
            Cost: 2, UpgradedCost: 1,
            Effects: System.Array.Empty<CardEffect>(),
            UpgradedEffects: null,
            Keywords: null);
        Assert.True(def.IsUpgradable);
    }

    [Fact]
    public void IsUpgradable_true_when_upgradedEffects_only()
    {
        var def = new CardDefinition(
            "x", "x", null, CardRarity.Common, CardType.Skill,
            Cost: 1, UpgradedCost: null,
            Effects: System.Array.Empty<CardEffect>(),
            UpgradedEffects: System.Array.Empty<CardEffect>(),
            Keywords: null);
        Assert.True(def.IsUpgradable);
    }

    [Fact]
    public void Keywords_default_to_null()
    {
        var def = new CardDefinition(
            "x", "x", null, CardRarity.Common, CardType.Skill,
            Cost: 1, UpgradedCost: null,
            Effects: System.Array.Empty<CardEffect>(),
            UpgradedEffects: null,
            Keywords: null);
        Assert.Null(def.Keywords);
    }

    [Fact]
    public void Keywords_can_hold_wild()
    {
        var def = new CardDefinition(
            "x", "x", null, CardRarity.Common, CardType.Skill,
            Cost: 5, UpgradedCost: null,
            Effects: System.Array.Empty<CardEffect>(),
            UpgradedEffects: null,
            Keywords: new[] { "wild" });
        Assert.NotNull(def.Keywords);
        Assert.Contains("wild", def.Keywords);
    }
}
