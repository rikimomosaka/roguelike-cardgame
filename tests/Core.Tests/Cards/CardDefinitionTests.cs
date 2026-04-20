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
            Effects: new List<CardEffect> { new DamageEffect(6) },
            UpgradedEffects: new List<CardEffect> { new DamageEffect(9) });

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
            Effects: new List<CardEffect>(),
            UpgradedEffects: null);

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
            Effects: new List<CardEffect> { new DamageEffect(6) },
            UpgradedEffects: null);

        Assert.Equal("ストライク(周年記念)", def.DisplayName);
        Assert.Equal("ストライク", def.Name);
    }
}
