using System.Collections.Immutable;
using RoguelikeCardGame.Core.Map;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Map;

public class MapGenerationConfigTests
{
    [Fact]
    public void IntRange_EqualsByValue()
    {
        Assert.Equal(new IntRange(1, 3), new IntRange(1, 3));
    }

    [Fact]
    public void TileKindPair_EqualsByValue()
    {
        Assert.Equal(
            new TileKindPair(TileKind.Rest, TileKind.Rest),
            new TileKindPair(TileKind.Rest, TileKind.Rest));
    }

    [Fact]
    public void MapGenerationConfig_ConstructsWithAllFields()
    {
        var config = new MapGenerationConfig(
            RowCount: 15,
            ColumnCount: 5,
            RowNodeCountMin: 2,
            RowNodeCountMax: 4,
            EdgeWeights: new EdgeCountWeights(82, 16, 2),
            TileDistribution: new TileDistributionRule(
                BaseWeights: ImmutableDictionary<TileKind, double>.Empty,
                MinPerMap: ImmutableDictionary<TileKind, int>.Empty,
                MaxPerMap: ImmutableDictionary<TileKind, int>.Empty),
            FixedRows: ImmutableArray.Create(new FixedRowRule(9, TileKind.Treasure)),
            RowKindExclusions: ImmutableArray.Create(new RowKindExclusion(14, TileKind.Rest)),
            PathConstraints: new PathConstraintRule(
                PerPathCount: ImmutableDictionary<TileKind, IntRange>.Empty,
                MinEliteRow: 6,
                ForbiddenConsecutive: ImmutableArray<TileKindPair>.Empty),
            MaxRegenerationAttempts: 100);

        Assert.Equal(15, config.RowCount);
        Assert.Equal(9, config.FixedRows[0].Row);
    }
}
