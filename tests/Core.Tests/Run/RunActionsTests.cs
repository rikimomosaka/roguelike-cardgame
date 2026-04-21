using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class RunActionsTests
{
    private static (DungeonMap map, RunState state) SetUp()
    {
        var cfg = MapGenerationConfigLoader.LoadAct1();
        var map = new DungeonMapGenerator().Generate(new SystemRng(58), cfg);
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var state = RunState.NewSoloRun(
            catalog,
            rngSeed: 58UL,
            startNodeId: map.StartNodeId,
            unknownResolutions: ImmutableDictionary<int, TileKind>.Empty,
            nowUtc: new DateTimeOffset(2026, 4, 21, 0, 0, 0, TimeSpan.Zero));
        return (map, state);
    }

    [Fact]
    public void SelectNextNode_ValidAdjacent_UpdatesCurrentAndAppendsVisited()
    {
        var (map, state) = SetUp();
        int target = map.GetNode(map.StartNodeId).OutgoingNodeIds[0];
        var next = RunActions.SelectNextNode(state, map, target);
        Assert.Equal(target, next.CurrentNodeId);
        Assert.Equal(new[] { map.StartNodeId, target }, next.VisitedNodeIds.ToArray());
    }

    [Fact]
    public void SelectNextNode_NonAdjacent_Throws()
    {
        var (map, state) = SetUp();
        int start = map.StartNodeId;
        int nonAdjacent = Enumerable.Range(0, map.Nodes.Length)
            .First(id => id != start && !map.GetNode(start).OutgoingNodeIds.Contains(id));
        Assert.Throws<ArgumentException>(() => RunActions.SelectNextNode(state, map, nonAdjacent));
    }

    [Fact]
    public void SelectNextNode_OutOfRange_Throws()
    {
        var (map, state) = SetUp();
        Assert.Throws<ArgumentException>(() => RunActions.SelectNextNode(state, map, -1));
        Assert.Throws<ArgumentException>(() => RunActions.SelectNextNode(state, map, map.Nodes.Length));
    }

    [Fact]
    public void SelectNextNode_DoesNotMutatePlaySeconds()
    {
        var (map, state) = SetUp();
        int target = map.GetNode(map.StartNodeId).OutgoingNodeIds[0];
        var next = RunActions.SelectNextNode(state, map, target);
        Assert.Equal(state.PlaySeconds, next.PlaySeconds);
    }
}
