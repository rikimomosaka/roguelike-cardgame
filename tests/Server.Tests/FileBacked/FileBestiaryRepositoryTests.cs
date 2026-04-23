using System;
using System.Collections.Immutable;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using RoguelikeCardGame.Core.Bestiary;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.History;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Services;
using RoguelikeCardGame.Server.Services.FileBacked;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.FileBacked;

public class FileBestiaryRepositoryTests : IDisposable
{
    private readonly string _root;
    private readonly FileBestiaryRepository _repo;

    public FileBestiaryRepositoryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "roguelike-bestiary-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var opts = Options.Create(new DataStorageOptions { RootDirectory = _root });
        _repo = new FileBestiaryRepository(opts);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsEmpty()
    {
        var loaded = await _repo.LoadAsync("aaa", default);
        Assert.Equal(BestiaryState.Empty, loaded);
    }

    [Fact]
    public async Task Save_Then_Load_Roundtrip()
    {
        var state = BestiaryState.Empty with
        {
            DiscoveredCardBaseIds = ImmutableHashSet.Create("strike"),
        };
        await _repo.SaveAsync("bbb", state, default);
        var loaded = await _repo.LoadAsync("bbb", default);
        Assert.True(state.DiscoveredCardBaseIds.SetEquals(loaded.DiscoveredCardBaseIds));
        Assert.True(state.DiscoveredRelicIds.SetEquals(loaded.DiscoveredRelicIds));
        Assert.True(state.DiscoveredPotionIds.SetEquals(loaded.DiscoveredPotionIds));
        Assert.True(state.EncounteredEnemyIds.SetEquals(loaded.EncounteredEnemyIds));
        Assert.Equal(state.SchemaVersion, loaded.SchemaVersion);
    }

    [Fact]
    public async Task MergeAsync_AppliesRecordToCurrent()
    {
        var rec = new RunHistoryRecord(
            SchemaVersion: RunHistoryRecord.CurrentSchemaVersion,
            AccountId: "ccc", RunId: "r", Outcome: RunProgress.Cleared,
            ActReached: 1, NodesVisited: 0, PlaySeconds: 0L, CharacterId: "default",
            FinalHp: 80, FinalMaxHp: 80, FinalGold: 99,
            FinalDeck: ImmutableArray<CardInstance>.Empty,
            FinalRelics: ImmutableArray<string>.Empty,
            EndedAtUtc: DateTimeOffset.UnixEpoch,
            SeenCardBaseIds: ImmutableArray.Create("strike"),
            AcquiredRelicIds: ImmutableArray<string>.Empty,
            AcquiredPotionIds: ImmutableArray<string>.Empty,
            EncounteredEnemyIds: ImmutableArray.Create("jaw_worm"));
        await _repo.MergeAsync("ccc", rec, default);
        var loaded = await _repo.LoadAsync("ccc", default);
        Assert.Contains("strike", loaded.DiscoveredCardBaseIds);
        Assert.Contains("jaw_worm", loaded.EncounteredEnemyIds);
    }
}
