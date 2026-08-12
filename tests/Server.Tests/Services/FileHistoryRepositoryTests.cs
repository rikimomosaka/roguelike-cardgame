using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.History;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Services;
using RoguelikeCardGame.Server.Services.FileBacked;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Services;

public class FileHistoryRepositoryTests
{
    [Fact]
    public async Task AppendAndList_RoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hist_" + Guid.NewGuid().ToString("N"));
        var opts = Options.Create(new DataStorageOptions { RootDirectory = dir });
        IHistoryRepository repo = new FileHistoryRepository(opts);

        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = RunState.NewSoloRun(
            cat,
            rngSeed: 1UL,
            startNodeId: 0,
            unknownResolutions: System.Collections.Immutable.ImmutableDictionary<int, RoguelikeCardGame.Core.Map.TileKind>.Empty,
            encounterQueueWeak: System.Collections.Immutable.ImmutableArray<string>.Empty,
            encounterQueueStrong: System.Collections.Immutable.ImmutableArray<string>.Empty,
            encounterQueueElite: System.Collections.Immutable.ImmutableArray<string>.Empty,
            encounterQueueBoss: System.Collections.Immutable.ImmutableArray<string>.Empty,
            nowUtc: DateTimeOffset.UtcNow);
        var fakeMap = new RoguelikeCardGame.Core.Map.DungeonMap(
            StartNodeId: 0,
            BossNodeId: 0,
            Nodes: System.Collections.Immutable.ImmutableArray.Create(new RoguelikeCardGame.Core.Map.MapNode(
                Id: 0, Row: 0, Column: 0,
                Kind: RoguelikeCardGame.Core.Map.TileKind.Start,
                OutgoingNodeIds: System.Collections.Immutable.ImmutableArray<int>.Empty)));
        var rec = RunHistoryBuilder.From("acc1", s, fakeMap, RunProgress.Cleared);

        await repo.AppendAsync("acc1", rec, CancellationToken.None);
        var list = await repo.ListAsync("acc1", CancellationToken.None);
        Assert.Single(list);
        Assert.Equal(rec.RunId, list[0].RunId);

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task Append_same_RunId_is_idempotent_across_retry_and_concurrency()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hist_" + Guid.NewGuid().ToString("N"));
        var repo = new FileHistoryRepository(
            Options.Create(new DataStorageOptions { RootDirectory = dir }));
        var first = Record("same-run", DateTimeOffset.UnixEpoch);
        var retried = first with { EndedAtUtc = DateTimeOffset.UnixEpoch.AddMinutes(1) };

        var canonical = await repo.AppendAsync("acc", first, CancellationToken.None);
        var retryResults = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ =>
            repo.AppendAsync("acc", retried, CancellationToken.None)));

        Assert.Equal(first, canonical);
        Assert.All(retryResults, result => Assert.Equal(first, result));
        var list = await repo.ListAsync("acc", CancellationToken.None);
        Assert.Single(list);
        Assert.Equal("same-run", list[0].RunId);
        Assert.Equal(first.EndedAtUtc, list[0].EndedAtUtc);
        Assert.Empty(Directory.EnumerateFiles(dir, "*.tmp.*", SearchOption.AllDirectories));
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task Append_distinct_RunIds_keeps_both_records()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hist_" + Guid.NewGuid().ToString("N"));
        var repo = new FileHistoryRepository(
            Options.Create(new DataStorageOptions { RootDirectory = dir }));

        await repo.AppendAsync("acc", Record("run-a", DateTimeOffset.UnixEpoch),
            CancellationToken.None);
        await repo.AppendAsync("acc", Record("run-b", DateTimeOffset.UnixEpoch.AddSeconds(1)),
            CancellationToken.None);

        var list = await repo.ListAsync("acc", CancellationToken.None);
        Assert.Equal(2, list.Count);
        Assert.Equal(new[] { "run-b", "run-a" }, list.Select(x => x.RunId));
        Directory.Delete(dir, recursive: true);
    }

    private static RunHistoryRecord Record(string runId, DateTimeOffset endedAt) => new(
        SchemaVersion: RunHistoryRecord.CurrentSchemaVersion,
        AccountId: "acc",
        RunId: runId,
        Outcome: RunProgress.Cleared,
        ActReached: 1,
        NodesVisited: 0,
        PlaySeconds: 0,
        CharacterId: "default",
        FinalHp: 80,
        FinalMaxHp: 80,
        FinalGold: 99,
        FinalDeck: ImmutableArray<CardInstance>.Empty,
        FinalRelics: ImmutableArray<string>.Empty,
        EndedAtUtc: endedAt,
        SeenCardBaseIds: ImmutableArray<string>.Empty,
        AcquiredRelicIds: ImmutableArray<string>.Empty,
        AcquiredPotionIds: ImmutableArray<string>.Empty,
        EncounteredEnemyIds: ImmutableArray<string>.Empty,
        JourneyLog: ImmutableArray<JourneyEntry>.Empty);
}
