using System;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class RunStateSerializerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 4, 20, 12, 0, 0, TimeSpan.Zero);

    private static RunState FreshRun()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        return RunState.NewSoloRun(catalog, rngSeed: 42UL, nowUtc: FixedNow);
    }

    [Fact]
    public void SerializeThenDeserialize_RoundTripsAllFields()
    {
        var original = FreshRun();

        var json = RunStateSerializer.Serialize(original);
        var restored = RunStateSerializer.Deserialize(json);

        Assert.Equal(original.SchemaVersion, restored.SchemaVersion);
        Assert.Equal(original.CurrentAct, restored.CurrentAct);
        Assert.Equal(original.CurrentTileIndex, restored.CurrentTileIndex);
        Assert.Equal(original.CurrentHp, restored.CurrentHp);
        Assert.Equal(original.MaxHp, restored.MaxHp);
        Assert.Equal(original.Gold, restored.Gold);
        Assert.Equal(original.Deck, restored.Deck);
        Assert.Equal(original.Relics, restored.Relics);
        Assert.Equal(original.Potions, restored.Potions);
        Assert.Equal(original.PlaySeconds, restored.PlaySeconds);
        Assert.Equal(original.RngSeed, restored.RngSeed);
        Assert.Equal(original.SavedAtUtc, restored.SavedAtUtc);
        Assert.Equal(original.Progress, restored.Progress);
    }

    [Fact]
    public void Serialize_UsesCamelCaseAndStringEnum()
    {
        var json = RunStateSerializer.Serialize(FreshRun());
        Assert.Contains("\"schemaVersion\":1", json);
        Assert.Contains("\"progress\":\"InProgress\"", json);
        Assert.DoesNotContain("\"SchemaVersion\"", json); // PascalCase は出ない
    }

    [Fact]
    public void Deserialize_BrokenJson_Throws()
    {
        var ex = Assert.Throws<RunStateSerializerException>(
            () => RunStateSerializer.Deserialize("{ not valid"));
        Assert.Contains("パース", ex.Message);
    }

    [Fact]
    public void Deserialize_NullLiteral_Throws()
    {
        var ex = Assert.Throws<RunStateSerializerException>(
            () => RunStateSerializer.Deserialize("null"));
        Assert.Contains("null", ex.Message);
    }

    [Fact]
    public void Deserialize_UnknownField_Throws()
    {
        var json = """
        {
          "schemaVersion": 1,
          "currentAct": 1,
          "currentTileIndex": 0,
          "currentHp": 80,
          "maxHp": 80,
          "gold": 99,
          "deck": [],
          "relics": [],
          "potions": [],
          "playSeconds": 0,
          "rngSeed": 0,
          "savedAtUtc": "2026-04-20T12:00:00+00:00",
          "progress": "InProgress",
          "unknownField": 42
        }
        """;

        var ex = Assert.Throws<RunStateSerializerException>(
            () => RunStateSerializer.Deserialize(json));
        Assert.Contains("パース", ex.Message);
    }

    [Fact]
    public void Deserialize_WrongSchemaVersion_Throws()
    {
        // 手書きで schemaVersion=99 の RunState を作る
        var json = """
        {
          "schemaVersion": 99,
          "currentAct": 1,
          "currentTileIndex": 0,
          "currentHp": 80,
          "maxHp": 80,
          "gold": 99,
          "deck": [],
          "relics": [],
          "potions": [],
          "playSeconds": 0,
          "rngSeed": 0,
          "savedAtUtc": "2026-04-20T12:00:00+00:00",
          "progress": "InProgress"
        }
        """;

        var ex = Assert.Throws<RunStateSerializerException>(
            () => RunStateSerializer.Deserialize(json));
        Assert.Contains("schemaVersion", ex.Message);
        Assert.Contains("99", ex.Message);
    }
}
