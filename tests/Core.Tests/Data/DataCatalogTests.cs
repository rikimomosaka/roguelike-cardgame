using System.Collections.Generic;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Tests.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Data;

public class DataCatalogTests
{
    [Fact]
    public void LoadFromStrings_BuildsAllFourDictionaries()
    {
        var catalog = DataCatalog.LoadFromStrings(
            cards: new[] { JsonFixtures.StrikeJson, JsonFixtures.DefendJson },
            relics: new[] { JsonFixtures.BurningBloodJson, JsonFixtures.LanternJson },
            potions: new[] { JsonFixtures.BlockPotionJson, JsonFixtures.FirePotionJson },
            enemies: new[] { JsonFixtures.JawWormJson, JsonFixtures.GremlinNobJson });

        Assert.Equal(2, catalog.Cards.Count);
        Assert.Equal(2, catalog.Relics.Count);
        Assert.Equal(2, catalog.Potions.Count);
        Assert.Equal(2, catalog.Enemies.Count);
        Assert.Equal("ストライク", catalog.Cards["strike"].Name);
        Assert.Equal("グレムリン・ノブ", catalog.Enemies["gremlin_nob"].Name);
    }

    [Fact]
    public void DuplicateCardId_Throws()
    {
        var ex = Assert.Throws<DataCatalogException>(() =>
            DataCatalog.LoadFromStrings(
                cards: new[] { JsonFixtures.StrikeJson, JsonFixtures.StrikeJson },
                relics: System.Array.Empty<string>(),
                potions: System.Array.Empty<string>(),
                enemies: System.Array.Empty<string>()));
        Assert.Contains("strike", ex.Message);
    }

    [Fact]
    public void EmptyInputs_ReturnsEmptyCatalog()
    {
        var catalog = DataCatalog.LoadFromStrings(
            cards: System.Array.Empty<string>(),
            relics: System.Array.Empty<string>(),
            potions: System.Array.Empty<string>(),
            enemies: System.Array.Empty<string>());
        Assert.Empty(catalog.Cards);
        Assert.Empty(catalog.Relics);
        Assert.Empty(catalog.Potions);
        Assert.Empty(catalog.Enemies);
    }
}
