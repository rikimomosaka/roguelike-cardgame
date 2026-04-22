using RoguelikeCardGame.Core.Data;
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
    public void Act1StartRelics_AreDefinedInCatalog()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        foreach (var id in cat.ActStartRelicPools![1])
            Assert.True(cat.Relics.ContainsKey(id), $"Relic '{id}' not found");
    }
}
