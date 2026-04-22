using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
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
        var rec = RunHistoryBuilder.From("acc1", s, 3, RunProgress.Cleared);

        await repo.AppendAsync("acc1", rec, CancellationToken.None);
        var list = await repo.ListAsync("acc1", CancellationToken.None);
        Assert.Single(list);
        Assert.Equal(rec.RunId, list[0].RunId);

        Directory.Delete(dir, recursive: true);
    }
}
