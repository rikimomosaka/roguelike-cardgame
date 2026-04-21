using System;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class RunStateValidateTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 4, 21, 0, 0, 0, TimeSpan.Zero);

    private static RunState ValidBase() =>
        RunState.NewSoloRun(
            EmbeddedDataLoader.LoadCatalog(),
            rngSeed: 1UL,
            startNodeId: 0,
            unknownResolutions: ImmutableDictionary<int, TileKind>.Empty,
            nowUtc: FixedNow);

    [Fact]
    public void Validate_WhenValid_ReturnsNull()
    {
        Assert.Null(ValidBase().Validate());
    }

    [Fact]
    public void Validate_WrongSchemaVersion_ReturnsMessage()
    {
        var broken = ValidBase() with { SchemaVersion = 1 };
        var msg = broken.Validate();
        Assert.NotNull(msg);
        Assert.Contains("SchemaVersion", msg);
    }

    [Fact]
    public void Validate_VisitedNodeIds_IsDefault_ReturnsMessage()
    {
        var broken = ValidBase() with { VisitedNodeIds = default };
        var msg = broken.Validate();
        Assert.NotNull(msg);
        Assert.Contains("VisitedNodeIds", msg);
    }

    [Fact]
    public void Validate_CurrentNodeId_NotInVisited_ReturnsMessage()
    {
        var broken = ValidBase() with
        {
            CurrentNodeId = 99,
            VisitedNodeIds = ImmutableArray.Create(0),
        };
        var msg = broken.Validate();
        Assert.NotNull(msg);
        Assert.Contains("CurrentNodeId", msg);
    }

    [Fact]
    public void Validate_VisitedNodeIds_HasDuplicate_ReturnsMessage()
    {
        var broken = ValidBase() with
        {
            VisitedNodeIds = ImmutableArray.Create(0, 1, 1),
        };
        var msg = broken.Validate();
        Assert.NotNull(msg);
        Assert.Contains("duplicates", msg);
    }

    [Fact]
    public void Validate_UnknownResolutions_ContainsUnknownValue_ReturnsMessage()
    {
        var broken = ValidBase() with
        {
            UnknownResolutions = ImmutableDictionary<int, TileKind>.Empty.Add(5, TileKind.Unknown),
        };
        var msg = broken.Validate();
        Assert.NotNull(msg);
        Assert.Contains("UnknownResolutions", msg);
    }

    [Fact]
    public void Validate_UnknownResolutions_ContainsBossValue_ReturnsMessage()
    {
        var broken = ValidBase() with
        {
            UnknownResolutions = ImmutableDictionary<int, TileKind>.Empty.Add(5, TileKind.Boss),
        };
        var msg = broken.Validate();
        Assert.NotNull(msg);
        Assert.Contains("UnknownResolutions", msg);
    }
}
