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
}
