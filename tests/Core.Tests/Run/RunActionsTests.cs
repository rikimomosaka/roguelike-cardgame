using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Events;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Merchant;
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
            encounterQueueWeak: ImmutableArray<string>.Empty,
            encounterQueueStrong: ImmutableArray<string>.Empty,
            encounterQueueElite: ImmutableArray<string>.Empty,
            encounterQueueBoss: ImmutableArray<string>.Empty,
            nowUtc: new DateTimeOffset(2026, 4, 21, 0, 0, 0, TimeSpan.Zero));
        return (map, state with
        {
            VisitedNodeIds = ImmutableArray.Create(map.StartNodeId),
        });
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

    [Fact]
    public void SelectNextNode_UnresolvedEvent_Throws()
    {
        var (map, state) = SetUp();
        int target = map.GetNode(map.StartNodeId).OutgoingNodeIds[0];
        var eventInstance = new EventInstance(
            "test", ImmutableArray<EventChoice>.Empty, ChosenIndex: null);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            RunActions.SelectNextNode(state with { ActiveEvent = eventInstance }, map, target));

        Assert.Contains("event", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectNextNode_ResolvedEvent_AllowsMove()
    {
        var (map, state) = SetUp();
        int target = map.GetNode(map.StartNodeId).OutgoingNodeIds[0];
        var eventInstance = new EventInstance(
            "test", ImmutableArray<EventChoice>.Empty, ChosenIndex: 0);

        var next = RunActions.SelectNextNode(
            state with { ActiveEvent = eventInstance }, map, target);

        Assert.Equal(target, next.CurrentNodeId);
    }

    [Fact]
    public void SelectNextNode_UncompletedRest_Throws()
    {
        var (map, state) = SetUp();
        int target = map.GetNode(map.StartNodeId).OutgoingNodeIds[0];

        var ex = Assert.Throws<InvalidOperationException>(() =>
            RunActions.SelectNextNode(
                state with { ActiveRestPending = true, ActiveRestCompleted = false },
                map,
                target));

        Assert.Contains("rest", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectNextNode_CompletedRest_AllowsMove()
    {
        var (map, state) = SetUp();
        int target = map.GetNode(map.StartNodeId).OutgoingNodeIds[0];

        var next = RunActions.SelectNextNode(
            state with { ActiveRestPending = true, ActiveRestCompleted = true },
            map,
            target);

        Assert.Equal(target, next.CurrentNodeId);
    }

    [Fact]
    public void SelectNextNode_PendingActStartRelicChoice_Throws()
    {
        var (map, state) = SetUp();
        int target = map.GetNode(map.StartNodeId).OutgoingNodeIds[0];
        var choice = new ActStartRelicChoice(
            ImmutableArray.Create("relic_a", "relic_b", "relic_c"));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            RunActions.SelectNextNode(
                state with { ActiveActStartRelicChoice = choice }, map, target));

        Assert.Contains("relic", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectNextNode_ActiveMerchant_AllowsMove()
    {
        var (map, state) = SetUp();
        int target = map.GetNode(map.StartNodeId).OutgoingNodeIds[0];
        var merchant = new MerchantInventory(
            ImmutableArray<MerchantOffer>.Empty,
            ImmutableArray<MerchantOffer>.Empty,
            ImmutableArray<MerchantOffer>.Empty,
            DiscardSlotUsed: false,
            DiscardPrice: 75);

        var next = RunActions.SelectNextNode(
            state with { ActiveMerchant = merchant }, map, target);

        Assert.Equal(target, next.CurrentNodeId);
    }

    [Fact]
    public void SelectNextNode_FinishedRun_Throws()
    {
        var (map, state) = SetUp();
        int target = map.GetNode(map.StartNodeId).OutgoingNodeIds[0];

        var ex = Assert.Throws<InvalidOperationException>(() =>
            RunActions.SelectNextNode(
                state with { Progress = RunProgress.GameOver }, map, target));

        Assert.Contains("ended", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectNextNode_BeforeEnteringActStart_Throws()
    {
        var (map, state) = SetUp();
        int target = map.GetNode(map.StartNodeId).OutgoingNodeIds[0];

        var ex = Assert.Throws<InvalidOperationException>(() =>
            RunActions.SelectNextNode(
                state with { VisitedNodeIds = ImmutableArray<int>.Empty },
                map,
                target));

        Assert.Contains("act-start", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
