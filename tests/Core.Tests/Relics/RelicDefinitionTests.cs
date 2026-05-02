using System.Collections.Generic;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Relics;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Relics;

/// <summary>
/// Phase 10.5.L1.5: relic-level Trigger フィールド廃止に伴い、
/// trigger 付きで record を構築するテストは削除。発動タイミングは
/// CardEffect.Trigger (per-effect) で表現する。
/// </summary>
public class RelicDefinitionTests
{
    [Fact]
    public void EffectsList_isAccessible()
    {
        var def = new RelicDefinition(
            Id: "burning_blood",
            Name: "燃え盛る血",
            Rarity: CardRarity.Common,
            Effects: new List<CardEffect>
            {
                new CardEffect("healPercent", EffectScope.Self, null, 0, Trigger: "OnBattleEnd"),
            });

        Assert.Single(def.Effects);
        Assert.Equal("OnBattleEnd", def.Effects[0].Trigger);
    }

    [Fact]
    public void RelicCanHaveMultipleEffectsWithDifferentTriggers()
    {
        // Phase 10.5.L1.5: per-effect trigger により 1 個の relic で複数のタイミングを扱える
        var def = new RelicDefinition(
            Id: "multi",
            Name: "complex",
            Rarity: CardRarity.Rare,
            Effects: new List<CardEffect>
            {
                new CardEffect("gainMaxHp",  EffectScope.Self, null, 8, Trigger: "OnPickup"),
                new CardEffect("block",      EffectScope.Self, null, 5, Trigger: "OnBattleStart"),
                new CardEffect("draw",       EffectScope.Self, null, 1, Trigger: "OnTurnEnd"),
            });

        Assert.Equal(3, def.Effects.Count);
        Assert.Equal("OnPickup", def.Effects[0].Trigger);
        Assert.Equal("OnBattleStart", def.Effects[1].Trigger);
        Assert.Equal("OnTurnEnd", def.Effects[2].Trigger);
    }

    [Fact]
    public void Implemented_defaults_to_true()
    {
        var def = new RelicDefinition(
            Id: "r",
            Name: "name",
            Rarity: CardRarity.Common,
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
            Effects: new List<CardEffect>(),
            Description: "",
            Implemented: false);
        Assert.False(def.Implemented);
    }

    [Fact]
    public void Records_with_different_Implemented_are_not_equal()
    {
        var a = new RelicDefinition("r", "n", CardRarity.Common,
                                    new List<CardEffect>(), "", true);
        var b = new RelicDefinition("r", "n", CardRarity.Common,
                                    new List<CardEffect>(), "", false);
        Assert.NotEqual(a, b);
    }
}
