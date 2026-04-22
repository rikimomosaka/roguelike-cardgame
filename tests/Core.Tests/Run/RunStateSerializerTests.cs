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
    private static RunState SampleV4()
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
    public void RoundTrip_V4_PreservesAllFields()
    {
        var original = SampleV4();
        var json = RunStateSerializer.Serialize(original);
        var loaded = RunStateSerializer.Deserialize(json);
        Assert.Equal(4, loaded.SchemaVersion);
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
    public void Deserialize_V3Json_MigratesToV4_DeckBecomesCardInstances()
    {
        // v3: Deck は string[]。v4 では CardInstance[] に自動変換されるべき。
        var v3 = """
        {
          "schemaVersion": 3,
          "currentAct": 1,
          "currentNodeId": 0,
          "visitedNodeIds": [0],
          "unknownResolutions": {},
          "characterId": "default",
          "currentHp": 80,
          "maxHp": 80,
          "gold": 99,
          "deck": ["strike", "defend"],
          "potions": ["", "", ""],
          "potionSlotCount": 3,
          "activeBattle": null,
          "activeReward": null,
          "encounterQueueWeak": [],
          "encounterQueueStrong": [],
          "encounterQueueElite": [],
          "encounterQueueBoss": [],
          "rewardRngState": { "potionChancePercent": 40, "rareChanceBonusPercent": 0 },
          "relics": [],
          "playSeconds": 0,
          "rngSeed": 0,
          "savedAtUtc": "2026-04-21T00:00:00+00:00",
          "progress": "InProgress"
        }
        """;
        var loaded = RunStateSerializer.Deserialize(v3);
        Assert.Equal(4, loaded.SchemaVersion);
        Assert.Equal(2, loaded.Deck.Length);
        Assert.Equal("strike", loaded.Deck[0].Id);
        Assert.False(loaded.Deck[0].Upgraded);
        Assert.Equal("defend", loaded.Deck[1].Id);
        Assert.Null(loaded.ActiveMerchant);
        Assert.Null(loaded.ActiveEvent);
        Assert.False(loaded.ActiveRestPending);
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
        var original = SampleV4() with { SchemaVersion = 99 };
        var json = RunStateSerializer.Serialize(original);
        var ex = Assert.Throws<RunStateSerializerException>(() => RunStateSerializer.Deserialize(json));
        Assert.Contains("schemaVersion", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deserialize_V3WithMalformedDeck_Throws()
    {
        // deck 要素に数値が混ざっている。migration 時に RunStateSerializerException 経由で拒否されるべき。
        var v3 = """
        {
          "schemaVersion": 3,
          "currentAct": 1,
          "currentNodeId": 0,
          "visitedNodeIds": [0],
          "unknownResolutions": {},
          "characterId": "default",
          "currentHp": 80,
          "maxHp": 80,
          "gold": 99,
          "deck": ["strike", 42],
          "potions": ["", "", ""],
          "potionSlotCount": 3,
          "activeBattle": null,
          "activeReward": null,
          "encounterQueueWeak": [],
          "encounterQueueStrong": [],
          "encounterQueueElite": [],
          "encounterQueueBoss": [],
          "rewardRngState": { "potionChancePercent": 40, "rareChanceBonusPercent": 0 },
          "relics": [],
          "playSeconds": 0,
          "rngSeed": 0,
          "savedAtUtc": "2026-04-21T00:00:00+00:00",
          "progress": "InProgress"
        }
        """;
        Assert.Throws<RunStateSerializerException>(() => RunStateSerializer.Deserialize(v3));
    }
}
