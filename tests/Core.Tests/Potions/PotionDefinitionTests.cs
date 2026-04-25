using System.Collections.Generic;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Potions;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Potions;

public class PotionDefinitionTests
{
    [Fact]
    public void IsUsableOutsideBattle_true_when_any_effect_is_not_battleOnly()
    {
        var def = new PotionDefinition(
            "p", "n", CardRarity.Common,
            new List<CardEffect>
            {
                new("heal", EffectScope.Self, null, 10, BattleOnly: false),
            });
        Assert.True(def.IsUsableOutsideBattle);
    }

    [Fact]
    public void IsUsableOutsideBattle_false_when_all_effects_are_battleOnly()
    {
        var def = new PotionDefinition(
            "p", "n", CardRarity.Common,
            new List<CardEffect>
            {
                new("block", EffectScope.Self, null, 12, BattleOnly: true),
            });
        Assert.False(def.IsUsableOutsideBattle);
    }

    [Fact]
    public void IsUsableOutsideBattle_false_when_effects_empty()
    {
        var def = new PotionDefinition(
            "p", "n", CardRarity.Common,
            new List<CardEffect>());
        Assert.False(def.IsUsableOutsideBattle);
    }

    [Fact]
    public void IsUsableOutsideBattle_true_when_mixed_with_at_least_one_non_battleOnly()
    {
        var def = new PotionDefinition(
            "p", "n", CardRarity.Common,
            new List<CardEffect>
            {
                new("block", EffectScope.Self, null, 12, BattleOnly: true),
                new("heal", EffectScope.Self, null, 10, BattleOnly: false),
            });
        Assert.True(def.IsUsableOutsideBattle);
    }
}
