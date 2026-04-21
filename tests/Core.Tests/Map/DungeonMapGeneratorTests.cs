using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Map;

public class DungeonMapGeneratorTests
{
    private static MapGenerationConfig BaseConfig() => new(
        RowCount: 15,
        ColumnCount: 5,
        RowNodeCountMin: 2,
        RowNodeCountMax: 4,
        EdgeWeights: new EdgeCountWeights(82, 16, 2),
        TileDistribution: new TileDistributionRule(
            BaseWeights: new[]
            {
                new System.Collections.Generic.KeyValuePair<TileKind, double>(TileKind.Enemy, 45),
                new System.Collections.Generic.KeyValuePair<TileKind, double>(TileKind.Elite, 6),
                new System.Collections.Generic.KeyValuePair<TileKind, double>(TileKind.Rest, 12),
                new System.Collections.Generic.KeyValuePair<TileKind, double>(TileKind.Merchant, 5),
                new System.Collections.Generic.KeyValuePair<TileKind, double>(TileKind.Unknown, 32),
            }.ToImmutableDictionary(),
            MinPerMap: new[]
            {
                new System.Collections.Generic.KeyValuePair<TileKind, int>(TileKind.Merchant, 3),
                new System.Collections.Generic.KeyValuePair<TileKind, int>(TileKind.Elite, 2),
                new System.Collections.Generic.KeyValuePair<TileKind, int>(TileKind.Unknown, 6),
            }.ToImmutableDictionary(),
            MaxPerMap: new[]
            {
                new System.Collections.Generic.KeyValuePair<TileKind, int>(TileKind.Merchant, 3),
                new System.Collections.Generic.KeyValuePair<TileKind, int>(TileKind.Elite, 4),
                new System.Collections.Generic.KeyValuePair<TileKind, int>(TileKind.Unknown, 10),
            }.ToImmutableDictionary()),
        FixedRows: ImmutableArray.Create(
            new FixedRowRule(9, TileKind.Treasure),
            new FixedRowRule(15, TileKind.Rest)),
        RowKindExclusions: ImmutableArray.Create(
            new RowKindExclusion(14, TileKind.Rest)),
        PathConstraints: new PathConstraintRule(
            PerPathCount: new[]
            {
                new System.Collections.Generic.KeyValuePair<TileKind, IntRange>(TileKind.Enemy, new IntRange(4, 6)),
                new System.Collections.Generic.KeyValuePair<TileKind, IntRange>(TileKind.Elite, new IntRange(0, 2)),
                new System.Collections.Generic.KeyValuePair<TileKind, IntRange>(TileKind.Rest, new IntRange(1, 3)),
                new System.Collections.Generic.KeyValuePair<TileKind, IntRange>(TileKind.Merchant, new IntRange(1, 2)),
                new System.Collections.Generic.KeyValuePair<TileKind, IntRange>(TileKind.Treasure, new IntRange(1, 1)),
                new System.Collections.Generic.KeyValuePair<TileKind, IntRange>(TileKind.Unknown, new IntRange(3, 5)),
            }.ToImmutableDictionary(),
            MinEliteRow: 6,
            ForbiddenConsecutive: ImmutableArray.Create(new TileKindPair(TileKind.Rest, TileKind.Rest))),
        MaxRegenerationAttempts: 100);

    [Fact]
    public void Generate_HasStartAtRow0Column2()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        var start = map.GetNode(map.StartNodeId);
        Assert.Equal(0, start.Row);
        Assert.Equal(2, start.Column);
        Assert.Equal(TileKind.Start, start.Kind);
    }

    [Fact]
    public void Generate_HasBossAtRow16Column2()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        var boss = map.GetNode(map.BossNodeId);
        Assert.Equal(16, boss.Row);
        Assert.Equal(2, boss.Column);
        Assert.Equal(TileKind.Boss, boss.Kind);
    }

    [Fact]
    public void Generate_MiddleRowsHaveNodeCountInConfigRange()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        for (int r = 1; r <= 15; r++)
        {
            var count = map.NodesInRow(r).Count();
            Assert.InRange(count, 2, 4);
        }
    }

    [Fact]
    public void Generate_AllColumnsInRange()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        Assert.All(map.Nodes, n => Assert.InRange(n.Column, 0, 4));
    }

    [Fact]
    public void Generate_NodeIdsAreSequential()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        for (int i = 0; i < map.Nodes.Length; i++)
            Assert.Equal(i, map.Nodes[i].Id);
    }

    [Fact]
    public void Generate_NodesOrderedByRowThenColumn()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        for (int i = 1; i < map.Nodes.Length; i++)
        {
            var prev = map.Nodes[i - 1];
            var curr = map.Nodes[i];
            Assert.True(
                prev.Row < curr.Row || (prev.Row == curr.Row && prev.Column < curr.Column),
                $"Nodes not ordered: idx {i - 1}={prev.Row},{prev.Column} idx {i}={curr.Row},{curr.Column}");
        }
    }

    [Fact]
    public void Generate_RowNodeCountMinEqualsMax_AllRowsHaveExactCount()
    {
        var cfg = BaseConfig() with { RowNodeCountMin = 3, RowNodeCountMax = 3 };
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), cfg);
        for (int r = 1; r <= 15; r++)
            Assert.Equal(3, map.NodesInRow(r).Count());
    }

    [Fact]
    public void Generate_RowNodeCountEqualsColumnCount_NoDuplicateColumnsInRow()
    {
        var cfg = BaseConfig() with { RowNodeCountMin = 5, RowNodeCountMax = 5 };
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), cfg);
        for (int r = 1; r <= 15; r++)
        {
            var cols = map.NodesInRow(r).Select(n => n.Column).ToArray();
            Assert.Equal(5, cols.Length);
            Assert.Equal(cols.Distinct().Count(), cols.Length);  // no duplicates
            Assert.Equal(new[] { 0, 1, 2, 3, 4 }, cols.OrderBy(c => c));
        }
    }
}
