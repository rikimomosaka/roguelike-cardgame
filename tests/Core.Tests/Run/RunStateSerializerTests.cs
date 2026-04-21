using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class RunStateSerializerTests
{
    private static RunState SampleV2()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var state = RunState.NewSoloRun(
            catalog,
            rngSeed: 42UL,
            startNodeId: 0,
            unknownResolutions: ImmutableDictionary<int, TileKind>.Empty.Add(5, TileKind.Enemy),
            nowUtc: new DateTimeOffset(2026, 4, 21, 0, 0, 0, TimeSpan.Zero));
        return state;
    }

    [Fact]
    public void RoundTrip_V2_Preserves()
    {
        var original = SampleV2();
        var json = RunStateSerializer.Serialize(original);
        var loaded = RunStateSerializer.Deserialize(json);
        Assert.Equal(2, loaded.SchemaVersion);
        Assert.Equal(0, loaded.CurrentNodeId);
        Assert.Equal(new[] { 0 }, loaded.VisitedNodeIds.ToArray());
        Assert.Equal(TileKind.Enemy, loaded.UnknownResolutions[5]);
    }

    [Fact]
    public void Deserialize_V1Json_ThrowsSerializerException()
    {
        var v1 = "{\"schemaVersion\":1,\"currentAct\":1,\"currentTileIndex\":0,\"currentHp\":80,\"maxHp\":80,\"gold\":99,\"deck\":[],\"relics\":[],\"potions\":[],\"playSeconds\":0,\"rngSeed\":0,\"savedAtUtc\":\"2026-04-21T00:00:00+00:00\",\"progress\":\"InProgress\"}";
        Assert.Throws<RunStateSerializerException>(() => RunStateSerializer.Deserialize(v1));
    }

    [Fact]
    public void Deserialize_WrongSchemaVersionOnly_ThrowsSerializerException()
    {
        // v2 の shape で schemaVersion だけ 99 にした JSON。
        // UnmappedMemberHandling.Disallow に引っかからず、schemaVersion check に到達する。
        var original = SampleV2() with { SchemaVersion = 99 };
        var json = RunStateSerializer.Serialize(original);
        var ex = Assert.Throws<RunStateSerializerException>(() => RunStateSerializer.Deserialize(json));
        Assert.Contains("schemaVersion", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
