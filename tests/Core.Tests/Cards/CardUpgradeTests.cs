using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Cards;

public class CardUpgradeTests
{
    private static readonly DataCatalog Catalog = EmbeddedDataLoader.LoadCatalog();

    [Fact]
    public void CanUpgrade_UnupgradedCardWithUpgradedEffects_ReturnsTrue()
    {
        // "strike" は upgradedEffects を持つ
        var ci = new CardInstance("strike", Upgraded: false);
        Assert.True(CardUpgrade.CanUpgrade(ci, Catalog));
    }

    [Fact]
    public void CanUpgrade_AlreadyUpgraded_ReturnsFalse()
    {
        var ci = new CardInstance("strike", Upgraded: true);
        Assert.False(CardUpgrade.CanUpgrade(ci, Catalog));
    }

    [Fact]
    public void Upgrade_TogglesFlag()
    {
        var ci = new CardInstance("strike", Upgraded: false);
        var upgraded = CardUpgrade.Upgrade(ci);
        Assert.True(upgraded.Upgraded);
        Assert.Equal("strike", upgraded.Id);
    }

    [Fact]
    public void Upgrade_AlreadyUpgraded_Throws()
    {
        var ci = new CardInstance("strike", Upgraded: true);
        Assert.Throws<System.InvalidOperationException>(() => CardUpgrade.Upgrade(ci));
    }

    [Fact]
    public void CanUpgrade_ReturnsTrue_When_OnlyUpgradedCostIsSet()
    {
        var def = new CardDefinition(
            "x", "x", null, CardRarity.Common, CardType.Skill,
            Cost: 2, UpgradedCost: 1,
            Effects: System.Array.Empty<CardEffect>(),
            UpgradedEffects: null,
            Keywords: null);
        var catalog = new DataCatalog(
            Cards: new System.Collections.Generic.Dictionary<string, CardDefinition> { ["x"] = def },
            Relics: new System.Collections.Generic.Dictionary<string, RoguelikeCardGame.Core.Relics.RelicDefinition>(),
            Potions: new System.Collections.Generic.Dictionary<string, RoguelikeCardGame.Core.Potions.PotionDefinition>(),
            Enemies: new System.Collections.Generic.Dictionary<string, RoguelikeCardGame.Core.Battle.Definitions.EnemyDefinition>(),
            Encounters: new System.Collections.Generic.Dictionary<string, RoguelikeCardGame.Core.Data.EncounterDefinition>(),
            RewardTables: new System.Collections.Generic.Dictionary<string, RoguelikeCardGame.Core.Data.RewardTable>(),
            Characters: new System.Collections.Generic.Dictionary<string, RoguelikeCardGame.Core.Data.CharacterDefinition>(),
            Events: new System.Collections.Generic.Dictionary<string, RoguelikeCardGame.Core.Events.EventDefinition>());
        var ci = new CardInstance("x", Upgraded: false);
        Assert.True(CardUpgrade.CanUpgrade(ci, catalog));
    }
}
