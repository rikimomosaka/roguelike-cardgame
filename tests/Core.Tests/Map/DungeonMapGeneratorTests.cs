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
        RowNodeCountMin: 3,
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
                new System.Collections.Generic.KeyValuePair<TileKind, IntRange>(TileKind.Enemy, new IntRange(1, 12)),
                new System.Collections.Generic.KeyValuePair<TileKind, IntRange>(TileKind.Elite, new IntRange(0, 4)),
                new System.Collections.Generic.KeyValuePair<TileKind, IntRange>(TileKind.Rest, new IntRange(1, 6)),
                new System.Collections.Generic.KeyValuePair<TileKind, IntRange>(TileKind.Merchant, new IntRange(0, 3)),
                new System.Collections.Generic.KeyValuePair<TileKind, IntRange>(TileKind.Treasure, new IntRange(1, 1)),
                new System.Collections.Generic.KeyValuePair<TileKind, IntRange>(TileKind.Unknown, new IntRange(0, 10)),
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

    [Fact]
    public void Generate_StartConnectsToAllRow1Nodes()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        var start = map.GetNode(map.StartNodeId);
        var row1Ids = map.NodesInRow(1).Select(n => n.Id).OrderBy(i => i).ToArray();
        Assert.Equal(row1Ids, start.OutgoingNodeIds.OrderBy(i => i));
    }

    [Fact]
    public void Generate_Row15AllConnectToBoss()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        foreach (var n in map.NodesInRow(15))
        {
            Assert.Single(n.OutgoingNodeIds);
            Assert.Equal(map.BossNodeId, n.OutgoingNodeIds[0]);
        }
    }

    [Fact]
    public void Generate_BossHasNoOutgoingEdges()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        Assert.Empty(map.GetNode(map.BossNodeId).OutgoingNodeIds);
    }

    [Fact]
    public void Generate_MiddleEdgesRespectColumnAdjacency()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        for (int r = 1; r <= 14; r++)
        {
            foreach (var n in map.NodesInRow(r))
            {
                foreach (var dstId in n.OutgoingNodeIds)
                {
                    var dst = map.GetNode(dstId);
                    Assert.Equal(r + 1, dst.Row);
                    Assert.True(
                        System.Math.Abs(n.Column - dst.Column) <= 1,
                        $"Edge {n.Id}(row={r}, col={n.Column}) -> {dst.Id}(col={dst.Column}) violates ±1 adjacency");
                }
            }
        }
    }

    [Fact]
    public void Generate_MiddleOutDegreeBetween1And3()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        for (int r = 1; r <= 14; r++)
        {
            foreach (var n in map.NodesInRow(r))
                Assert.InRange(n.OutgoingNodeIds.Length, 1, 3);
        }
    }

    [Fact]
    public void Generate_BossReachableFromStart()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        var visited = new System.Collections.Generic.HashSet<int>();
        var stack = new System.Collections.Generic.Stack<int>();
        stack.Push(map.StartNodeId);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!visited.Add(id)) continue;
            foreach (var next in map.GetNode(id).OutgoingNodeIds)
                stack.Push(next);
        }
        Assert.Contains(map.BossNodeId, visited);
    }

    [Fact]
    public void Generate_OutgoingIdsAreSorted()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        foreach (var n in map.Nodes)
        {
            var sorted = n.OutgoingNodeIds.OrderBy(i => i).ToArray();
            Assert.Equal(sorted, n.OutgoingNodeIds.ToArray());
        }
    }

    [Fact]
    public void Generate_Row1AllEnemy()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        Assert.All(map.NodesInRow(1), n => Assert.Equal(TileKind.Enemy, n.Kind));
    }

    [Fact]
    public void Generate_Row9AllTreasure()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        Assert.All(map.NodesInRow(9), n => Assert.Equal(TileKind.Treasure, n.Kind));
    }

    [Fact]
    public void Generate_Row15AllRest()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        Assert.All(map.NodesInRow(15), n => Assert.Equal(TileKind.Rest, n.Kind));
    }

    [Fact]
    public void Generate_Row14HasNoRest()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        Assert.All(map.NodesInRow(14), n => Assert.NotEqual(TileKind.Rest, n.Kind));
    }

    [Fact]
    public void Generate_EliteOnlyInRow6OrLater()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        foreach (var n in map.Nodes.Where(n => n.Kind == TileKind.Elite))
            Assert.True(n.Row >= 6, $"Elite at row {n.Row} (< 6)");
    }

    [Fact]
    public void Generate_TileDistributionMinMaxPerMap()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        int merchants = map.Nodes.Count(n => n.Kind == TileKind.Merchant);
        int elites = map.Nodes.Count(n => n.Kind == TileKind.Elite);
        int unknowns = map.Nodes.Count(n => n.Kind == TileKind.Unknown);
        Assert.InRange(merchants, 3, 3);
        Assert.InRange(elites, 2, 4);
        Assert.InRange(unknowns, 6, 10);
    }

    [Fact]
    public void Generate_AllPathsSatisfyPerPathCount()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        foreach (var path in EnumeratePaths(map))
        {
            var counts = path.GroupBy(n => n.Kind).ToDictionary(g => g.Key, g => g.Count());
            foreach (var kv in BaseConfig().PathConstraints.PerPathCount)
            {
                int c = counts.GetValueOrDefault(kv.Key, 0);
                Assert.InRange(c, kv.Value.Min, kv.Value.Max);
            }
        }
    }

    [Fact]
    public void Generate_NoForbiddenConsecutivePairs()
    {
        var map = new DungeonMapGenerator().Generate(new SystemRng(42), BaseConfig());
        var forbidden = BaseConfig().PathConstraints.ForbiddenConsecutive;
        foreach (var path in EnumeratePaths(map))
        {
            for (int i = 0; i < path.Count - 1; i++)
            {
                foreach (var pair in forbidden)
                    Assert.False(
                        path[i].Kind == pair.First && path[i + 1].Kind == pair.Second,
                        $"Forbidden pair {pair.First}->{pair.Second} found at {path[i].Id}->{path[i + 1].Id}");
            }
        }
    }

    [Fact]
    public void Generate_Impossible_ThrowsMapGenerationException()
    {
        var baseConfig = BaseConfig();
        var impossible = baseConfig with
        {
            PathConstraints = baseConfig.PathConstraints with
            {
                PerPathCount = baseConfig.PathConstraints.PerPathCount
                    .SetItem(TileKind.Enemy, new IntRange(20, 30)),
            },
            MaxRegenerationAttempts = 5,
        };
        var ex = Assert.Throws<MapGenerationException>(
            () => new DungeonMapGenerator().Generate(new SystemRng(1), impossible));
        Assert.Equal(5, ex.AttemptCount);
        Assert.Contains("path-constraint", ex.FailureReason);
    }

    private static System.Collections.Generic.List<System.Collections.Generic.List<MapNode>> EnumeratePaths(DungeonMap map)
    {
        var results = new System.Collections.Generic.List<System.Collections.Generic.List<MapNode>>();
        var current = new System.Collections.Generic.List<MapNode>();

        void Dfs(int id)
        {
            var n = map.GetNode(id);
            current.Add(n);
            if (id == map.BossNodeId) results.Add(new System.Collections.Generic.List<MapNode>(current));
            else
                foreach (var next in n.OutgoingNodeIds) Dfs(next);
            current.RemoveAt(current.Count - 1);
        }

        Dfs(map.StartNodeId);
        return results;
    }

    [Fact]
    public void Generate_SameSeedAndConfig_ProducesIdenticalMap()
    {
        var cfg = BaseConfig();
        var a = new DungeonMapGenerator().Generate(new SystemRng(12345), cfg);
        var b = new DungeonMapGenerator().Generate(new SystemRng(12345), cfg);
        Assert.Equal(a.Nodes.Length, b.Nodes.Length);
        for (int i = 0; i < a.Nodes.Length; i++)
            Assert.Equal(a.Nodes[i], b.Nodes[i]);
        Assert.Equal(a.StartNodeId, b.StartNodeId);
        Assert.Equal(a.BossNodeId, b.BossNodeId);
    }

    [Fact]
    public void Generate_DifferentSeeds_ProduceDifferentMaps()
    {
        var cfg = BaseConfig();
        var a = new DungeonMapGenerator().Generate(new SystemRng(1), cfg);
        var b = new DungeonMapGenerator().Generate(new SystemRng(2), cfg);
        bool anyDiff =
            a.Nodes.Length != b.Nodes.Length ||
            !a.Nodes.Select(n => (n.Row, n.Column, n.Kind)).SequenceEqual(
                 b.Nodes.Select(n => (n.Row, n.Column, n.Kind)));
        Assert.True(anyDiff, "Different seeds produced identical map (extremely unlikely)");
    }

    [Fact]
    public void Generate_NullRng_Throws()
    {
        Assert.Throws<System.ArgumentNullException>(
            () => new DungeonMapGenerator().Generate(null!, BaseConfig()));
    }

    [Fact]
    public void Generate_NullConfig_Throws()
    {
        Assert.Throws<System.ArgumentNullException>(
            () => new DungeonMapGenerator().Generate(new SystemRng(1), null!));
    }
}
