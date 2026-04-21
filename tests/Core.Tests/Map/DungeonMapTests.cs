using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Map;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Map;

public class DungeonMapTests
{
    [Fact]
    public void MapNode_EqualsByValue()
    {
        var a = new MapNode(0, 1, 2, TileKind.Enemy, ImmutableArray.Create(1, 2));
        var b = new MapNode(0, 1, 2, TileKind.Enemy, ImmutableArray.Create(1, 2));
        Assert.Equal(a, b);
    }

    [Fact]
    public void DungeonMap_GetNode_ReturnsNodeById()
    {
        var nodes = ImmutableArray.Create(
            new MapNode(0, 0, 2, TileKind.Start, ImmutableArray.Create(1)),
            new MapNode(1, 1, 2, TileKind.Enemy, ImmutableArray<int>.Empty));
        var map = new DungeonMap(nodes, StartNodeId: 0, BossNodeId: 1);
        Assert.Equal(TileKind.Start, map.GetNode(0).Kind);
        Assert.Equal(TileKind.Enemy, map.GetNode(1).Kind);
    }

    [Fact]
    public void DungeonMap_NodesInRow_FiltersByRow()
    {
        var nodes = ImmutableArray.Create(
            new MapNode(0, 0, 2, TileKind.Start, ImmutableArray.Create(1, 2)),
            new MapNode(1, 1, 1, TileKind.Enemy, ImmutableArray<int>.Empty),
            new MapNode(2, 1, 3, TileKind.Enemy, ImmutableArray<int>.Empty));
        var map = new DungeonMap(nodes, 0, 2);
        var row1 = map.NodesInRow(1).ToList();
        Assert.Equal(2, row1.Count);
        Assert.All(row1, n => Assert.Equal(1, n.Row));
    }

    [Fact]
    public void TileKind_EnumValues_Exist()
    {
        Assert.True(System.Enum.IsDefined(typeof(TileKind), TileKind.Start));
        Assert.True(System.Enum.IsDefined(typeof(TileKind), TileKind.Enemy));
        Assert.True(System.Enum.IsDefined(typeof(TileKind), TileKind.Elite));
        Assert.True(System.Enum.IsDefined(typeof(TileKind), TileKind.Rest));
        Assert.True(System.Enum.IsDefined(typeof(TileKind), TileKind.Merchant));
        Assert.True(System.Enum.IsDefined(typeof(TileKind), TileKind.Treasure));
        Assert.True(System.Enum.IsDefined(typeof(TileKind), TileKind.Unknown));
        Assert.True(System.Enum.IsDefined(typeof(TileKind), TileKind.Boss));
    }
}
