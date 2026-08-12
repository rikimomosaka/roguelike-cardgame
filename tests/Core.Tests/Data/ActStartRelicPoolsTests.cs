using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Tests.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Data;

public class ActStartRelicPoolsTests
{
    [Fact]
    public void LoadCatalog_ExposesActStartRelicPools_ForAllActs()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        Assert.NotNull(cat.ActStartRelicPools);
        Assert.Equal(5, cat.ActStartRelicPools![1].Length);
        Assert.Equal(5, cat.ActStartRelicPools[2].Length);
        Assert.Equal(5, cat.ActStartRelicPools[3].Length);
    }

    [Fact]
    public void AllActStartRelics_AreDefinedInCatalog()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        foreach (var pool in cat.ActStartRelicPools!.Values)
            foreach (var id in pool)
                Assert.True(cat.Relics.ContainsKey(id), $"Relic '{id}' not found");
    }

    [Fact]
    public void LoadCatalog_RejectsUnknownActStartRelicId()
    {
        var exception = Assert.Throws<DataCatalogException>(() =>
            LoadWithPool("""{"act":1,"relicIds":["relic_a","relic_b","missing"]}"""));

        Assert.Contains("missing", exception.Message);
    }

    [Fact]
    public void LoadCatalog_RejectsDuplicateOrInsufficientActStartPool()
    {
        Assert.Throws<DataCatalogException>(() =>
            LoadWithPool("""{"act":1,"relicIds":["relic_a","relic_a","relic_b"]}"""));
        Assert.Throws<DataCatalogException>(() =>
            LoadWithPool("""{"act":1,"relicIds":["relic_a","relic_b"]}"""));
    }

    private static DataCatalog LoadWithPool(string poolJson)
    {
        var relicA = JsonFixtures.BurningBloodJson.Replace("burning_blood", "relic_a");
        var relicB = JsonFixtures.BurningBloodJson.Replace("burning_blood", "relic_b");
        var relicC = JsonFixtures.BurningBloodJson.Replace("burning_blood", "relic_c");
        return DataCatalog.LoadFromStrings(
            cards: System.Array.Empty<string>(),
            relics: new[] { relicA, relicB, relicC },
            potions: System.Array.Empty<string>(),
            enemies: System.Array.Empty<string>(),
            encounters: System.Array.Empty<string>(),
            rewardTables: System.Array.Empty<string>(),
            characters: System.Array.Empty<string>(),
            actStartRelicPools: new[] { poolJson });
    }
}
