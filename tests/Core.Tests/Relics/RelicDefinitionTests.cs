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
}
