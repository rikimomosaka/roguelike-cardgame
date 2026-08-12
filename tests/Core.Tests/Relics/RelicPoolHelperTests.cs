using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Relics;

namespace RoguelikeCardGame.Core.Tests.Relics;

public class RelicPoolHelperTests
{
    [Fact]
    public void GetActStartReservedIds_CombinesPoolsFromEveryAct()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();

        var actual = RelicPoolHelper.GetActStartReservedIds(catalog);
        var expected = catalog.ActStartRelicPools!.Values
            .SelectMany(pool => pool)
            .ToHashSet(System.StringComparer.Ordinal);

        Assert.Equal(expected.Count, actual.Count);
        Assert.True(expected.SetEquals(actual));
    }

    [Fact]
    public void GetNormalAcquisitionRelics_UsesPoolDataRatherThanIdPrefix()
    {
        const string reservedWithoutPrefix = "ordinary_name_reserved_by_data";
        const string prefixedButNotReserved = "act99_start_not_reserved_by_data";
        var catalog = EmbeddedDataLoader.LoadCatalog();
        catalog = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(
            catalog, reservedWithoutPrefix, System.Array.Empty<CardEffect>());
        catalog = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(
            catalog, prefixedButNotReserved, System.Array.Empty<CardEffect>());

        var pools = catalog.ActStartRelicPools!.ToDictionary(pair => pair.Key, pair => pair.Value);
        pools[99] = ImmutableArray.Create(reservedWithoutPrefix);
        catalog = catalog with { ActStartRelicPools = pools };

        var normalIds = RelicPoolHelper.GetNormalAcquisitionRelics(catalog)
            .Select(relic => relic.Id)
            .ToArray();

        Assert.DoesNotContain(reservedWithoutPrefix, normalIds);
        Assert.Contains(prefixedButNotReserved, normalIds);
    }
}
