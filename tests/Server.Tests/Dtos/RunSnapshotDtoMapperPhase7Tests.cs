using System.Collections.Immutable;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Dtos;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Dtos;

public class RunSnapshotDtoMapperPhase7Tests
{
    private static RunState FreshDefault(DataCatalog cat)
    {
        return RunState.NewSoloRun(
            cat,
            rngSeed: 0,
            startNodeId: 0,
            unknownResolutions: ImmutableDictionary<int, TileKind>.Empty,
            encounterQueueWeak: ImmutableArray<string>.Empty,
            encounterQueueStrong: ImmutableArray<string>.Empty,
            encounterQueueElite: ImmutableArray<string>.Empty,
            encounterQueueBoss: ImmutableArray<string>.Empty,
            nowUtc: DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public void Maps_ActiveActStartRelicChoice_WhenPresent()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = FreshDefault(cat) with
        {
            ActiveActStartRelicChoice = new ActStartRelicChoice(
                ImmutableArray.Create("a", "b", "c")),
        };
        var map = new DungeonMap(
            StartNodeId: s.CurrentNodeId,
            BossNodeId: s.CurrentNodeId + 100,
            Nodes: ImmutableArray.Create(new MapNode(
                Id: s.CurrentNodeId, Row: 0, Column: 0,
                Kind: TileKind.Start,
                OutgoingNodeIds: ImmutableArray<int>.Empty)));
        var dto = RunSnapshotDtoMapper.From(s, map, cat);
        Assert.NotNull(dto.Run.ActiveActStartRelicChoice);
        Assert.Equal(new[] { "a", "b", "c" }, dto.Run.ActiveActStartRelicChoice!.RelicIds);
    }
}
