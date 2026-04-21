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
    private static RunState SampleV3()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var state = RunState.NewSoloRun(
            catalog,
            rngSeed: 42UL,
            startNodeId: 0,
            unknownResolutions: ImmutableDictionary<int, TileKind>.Empty.Add(5, TileKind.Enemy),
            encounterQueueWeak: ImmutableArray.Create("encounter-weak-a", "encounter-weak-b"),
            encounterQueueStrong: ImmutableArray<string>.Empty,
            encounterQueueElite: ImmutableArray<string>.Empty,
            encounterQueueBoss: ImmutableArray<string>.Empty,
            nowUtc: new DateTimeOffset(2026, 4, 21, 0, 0, 0, TimeSpan.Zero));
        return state;
    }

    [Fact]
    public void RoundTrip_V3_Preserves()
    {
        var original = SampleV3();
        var json = RunStateSerializer.Serialize(original);
        var loaded = RunStateSerializer.Deserialize(json);
        Assert.Equal(3, loaded.SchemaVersion);
        Assert.Equal(0, loaded.CurrentNodeId);
        Assert.Equal(new[] { 0 }, loaded.VisitedNodeIds.ToArray());
        Assert.Equal(TileKind.Enemy, loaded.UnknownResolutions[5]);
        Assert.Equal("default", loaded.CharacterId);
        Assert.Equal(80, loaded.MaxHp);
        Assert.Equal(99, loaded.Gold);
        Assert.Equal(original.Deck.AsEnumerable(), loaded.Deck.AsEnumerable());
        Assert.Equal(original.Potions.AsEnumerable(), loaded.Potions.AsEnumerable());
        Assert.Equal(3, loaded.PotionSlotCount);
        Assert.Null(loaded.ActiveBattle);
        Assert.Null(loaded.ActiveReward);
        Assert.Equal(original.EncounterQueueWeak.AsEnumerable(), loaded.EncounterQueueWeak.AsEnumerable());
        Assert.Equal(original.RewardRngState, loaded.RewardRngState);
    }

    [Fact]
    public void Deserialize_V1Json_ThrowsSerializerException()
    {
        var v1 = "{\"schemaVersion\":1,\"currentAct\":1,\"currentTileIndex\":0,\"currentHp\":80,\"maxHp\":80,\"gold\":99,\"deck\":[],\"relics\":[],\"potions\":[],\"playSeconds\":0,\"rngSeed\":0,\"savedAtUtc\":\"2026-04-21T00:00:00+00:00\",\"progress\":\"InProgress\"}";
        Assert.Throws<RunStateSerializerException>(() => RunStateSerializer.Deserialize(v1));
    }

    [Fact]
    public void Deserialize_V2Json_ThrowsSerializerException()
    {
        // v2 shape: no CharacterId / Potions / ActiveBattle / ... fields.
        var v2 = "{\"schemaVersion\":2,\"currentAct\":1,\"currentNodeId\":0,\"visitedNodeIds\":[0],\"unknownResolutions\":{},\"currentHp\":80,\"maxHp\":80,\"gold\":99,\"deck\":[\"strike\"],\"relics\":[],\"potions\":[],\"playSeconds\":0,\"rngSeed\":0,\"savedAtUtc\":\"2026-04-21T00:00:00+00:00\",\"progress\":\"InProgress\"}";
        Assert.Throws<RunStateSerializerException>(() => RunStateSerializer.Deserialize(v2));
    }

    [Fact]
    public void Deserialize_WrongSchemaVersionOnly_ThrowsSerializerException()
    {
        // v3 の shape で schemaVersion だけ 99 にした JSON。
        // UnmappedMemberHandling.Disallow に引っかからず、schemaVersion check に到達する。
        var original = SampleV3() with { SchemaVersion = 99 };
        var json = RunStateSerializer.Serialize(original);
        var ex = Assert.Throws<RunStateSerializerException>(() => RunStateSerializer.Deserialize(json));
        Assert.Contains("schemaVersion", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
