using System;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Merchant;
using RoguelikeCardGame.Core.Rewards;
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
            encounterQueueWeak: ImmutableArray<string>.Empty,
            encounterQueueStrong: ImmutableArray<string>.Empty,
            encounterQueueElite: ImmutableArray<string>.Empty,
            encounterQueueBoss: ImmutableArray<string>.Empty,
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

    [Fact]
    public void Validate_AllActiveNull_ReturnsNull()
    {
        var s = SampleV4();
        Assert.Null(s.Validate());
    }

    [Fact]
    public void Validate_ActiveBattleAndActiveMerchant_ReturnsError()
    {
        var s = SampleV4() with
        {
            ActiveBattle = null,
            ActiveMerchant = FakeInventory(),
            ActiveReward = null,
        };
        // Active at most 1: start with merchant only — OK
        Assert.Null(s.Validate());

        var bad = s with { ActiveBattle = FakeBattle() };
        Assert.Contains("at most one", bad.Validate(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_RestPendingWithActiveReward_ReturnsError()
    {
        var s = SampleV4() with
        {
            ActiveRestPending = true,
            ActiveReward = FakeReward(),
        };
        Assert.Contains("ActiveRestPending", s.Validate());
    }

    private static RunState SampleV4()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        return RunState.NewSoloRun(
            catalog, rngSeed: 1UL, startNodeId: 0,
            unknownResolutions: ImmutableDictionary<int, TileKind>.Empty,
            encounterQueueWeak: ImmutableArray<string>.Empty,
            encounterQueueStrong: ImmutableArray<string>.Empty,
            encounterQueueElite: ImmutableArray<string>.Empty,
            encounterQueueBoss: ImmutableArray<string>.Empty,
            nowUtc: new DateTimeOffset(2026, 4, 22, 0, 0, 0, TimeSpan.Zero));
    }

    private static MerchantInventory FakeInventory() =>
        new(ImmutableArray<MerchantOffer>.Empty,
            ImmutableArray<MerchantOffer>.Empty,
            ImmutableArray<MerchantOffer>.Empty,
            DiscardSlotUsed: false, DiscardPrice: 75);

    private static BattleState FakeBattle() =>
        new BattleState("test", ImmutableArray<EnemyInstance>.Empty, BattleOutcome.Pending);

    private static RewardState FakeReward() =>
        new RewardState(0, false, null, true, ImmutableArray<string>.Empty, CardRewardStatus.Pending);
}
