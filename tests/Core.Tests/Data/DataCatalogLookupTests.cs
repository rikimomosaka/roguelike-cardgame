using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Tests.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Data;

public class DataCatalogLookupTests
{
    private static DataCatalog BuildCatalog() =>
        DataCatalog.LoadFromStrings(
            cards: new[] { JsonFixtures.StrikeJson, JsonFixtures.DefendJson },
            relics: new[] { JsonFixtures.BurningBloodJson },
            potions: new[] { JsonFixtures.BlockPotionJson },
            enemies: new[] { JsonFixtures.JawWormJson });

    [Fact]
    public void TryGetCard_Hit_ReturnsTrueAndDefinition()
    {
        var catalog = BuildCatalog();
        Assert.True(catalog.TryGetCard("strike", out var def));
        Assert.NotNull(def);
        Assert.Equal("ストライク", def!.Name);
    }

    [Fact]
    public void TryGetCard_Miss_ReturnsFalseAndNull()
    {
        var catalog = BuildCatalog();
        Assert.False(catalog.TryGetCard("nonexistent", out var def));
        Assert.Null(def);
    }

    [Fact]
    public void TryGetRelic_Potion_Enemy_AllWork()
    {
        var catalog = BuildCatalog();
        Assert.True(catalog.TryGetRelic("burning_blood", out _));
        Assert.True(catalog.TryGetPotion("block_potion", out _));
        Assert.True(catalog.TryGetEnemy("jaw_worm", out _));
        Assert.False(catalog.TryGetRelic("missing", out _));
        Assert.False(catalog.TryGetPotion("missing", out _));
        Assert.False(catalog.TryGetEnemy("missing", out _));
    }
}
