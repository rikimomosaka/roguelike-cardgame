using System.Collections.Generic;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Relics;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Relics;

public class RelicDefinitionTests
{
    [Fact]
    public void BurningBlood_IsOnBattleEnd()
    {
        var def = new RelicDefinition(
            Id: "burning_blood",
            Name: "燃え盛る血",
            Rarity: CardRarity.Common,
            Trigger: RelicTrigger.OnBattleEnd,
            Effects: new List<CardEffect> { new CardEffect("healPercent", EffectScope.Self, null, 0) });

        Assert.Equal(RelicTrigger.OnBattleEnd, def.Trigger);
    }

    [Fact]
    public void Lantern_IsPassive()
    {
        var def = new RelicDefinition(
            Id: "lantern",
            Name: "ランタン",
            Rarity: CardRarity.Common,
            Trigger: RelicTrigger.Passive,
            Effects: new List<CardEffect>());

        Assert.Equal(RelicTrigger.Passive, def.Trigger);
    }

    [Fact]
    public void Implemented_defaults_to_true()
    {
        var def = new RelicDefinition(
            Id: "r",
            Name: "name",
            Rarity: CardRarity.Common,
            Trigger: RelicTrigger.OnPickup,
            Effects: new List<CardEffect>());
        Assert.True(def.Implemented);
    }

    [Fact]
    public void Implemented_can_be_set_false()
    {
        var def = new RelicDefinition(
            Id: "r",
            Name: "name",
            Rarity: CardRarity.Common,
            Trigger: RelicTrigger.OnPickup,
            Effects: new List<CardEffect>(),
            Description: "",
            Implemented: false);
        Assert.False(def.Implemented);
    }

    [Fact]
    public void Records_with_different_Implemented_are_not_equal()
    {
        var a = new RelicDefinition("r", "n", CardRarity.Common, RelicTrigger.OnPickup,
                                    new List<CardEffect>(), "", true);
        var b = new RelicDefinition("r", "n", CardRarity.Common, RelicTrigger.OnPickup,
                                    new List<CardEffect>(), "", false);
        Assert.NotEqual(a, b);
    }
}
