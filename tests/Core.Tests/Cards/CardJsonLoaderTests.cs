using System.Linq;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Tests.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Cards;

public class CardJsonLoaderTests
{
    [Fact]
    public void ParseStrike_FillsAllFields()
    {
        var def = CardJsonLoader.Parse(JsonFixtures.StrikeJson);

        Assert.Equal("strike", def.Id);
        Assert.Equal("ストライク", def.Name);
        Assert.Null(def.DisplayName);
        Assert.Equal(CardRarity.Common, def.Rarity);
        Assert.Equal(CardType.Attack, def.CardType);
        Assert.Equal(1, def.Cost);

        var dmg = Assert.IsType<DamageEffect>(def.Effects.Single());
        Assert.Equal(6, dmg.Amount);

        Assert.NotNull(def.UpgradedEffects);
        var upDmg = Assert.IsType<DamageEffect>(def.UpgradedEffects!.Single());
        Assert.Equal(9, upDmg.Amount);
    }

    [Fact]
    public void ParseDefend_ParsesGainBlock()
    {
        var def = CardJsonLoader.Parse(JsonFixtures.DefendJson);
        var eff = Assert.IsType<GainBlockEffect>(def.Effects.Single());
        Assert.Equal(5, eff.Amount);
    }

    [Fact]
    public void ParseDisplayName_WhenProvided()
    {
        var def = CardJsonLoader.Parse(JsonFixtures.StrikePromoJson);
        Assert.Equal("ストライク(周年記念)", def.DisplayName);
        Assert.Equal(CardRarity.Promo, def.Rarity);
        Assert.Null(def.UpgradedEffects);
    }

    [Fact]
    public void ParseUnplayableCard_CostIsNull()
    {
        var def = CardJsonLoader.Parse(JsonFixtures.UnplayableCurseJson);
        Assert.Null(def.Cost);
        Assert.Empty(def.Effects);
    }

    [Fact]
    public void UnknownEffectType_IsPreservedAsUnknownEffect()
    {
        var def = CardJsonLoader.Parse(JsonFixtures.UnknownEffectJson);
        var eff = Assert.IsType<UnknownEffect>(def.Effects.Single());
        Assert.Equal("summonUnit", eff.Type);
    }

    [Fact]
    public void BrokenJson_ThrowsCardJsonException()
    {
        Assert.Throws<CardJsonException>(() => CardJsonLoader.Parse(JsonFixtures.BrokenJson));
    }
}
