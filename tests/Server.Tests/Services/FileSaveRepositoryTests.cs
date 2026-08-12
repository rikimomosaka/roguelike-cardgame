using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Services;
using RoguelikeCardGame.Server.Services.FileBacked;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Services;

public class FileSaveRepositoryTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly DataCatalog _catalog = EmbeddedDataLoader.LoadCatalog();
    private readonly FileSaveRepository _repo;
    private static readonly DateTimeOffset FixedNow = new(2026, 4, 20, 12, 0, 0, TimeSpan.Zero);

    public FileSaveRepositoryTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "rcg-save-tests-" + Guid.NewGuid().ToString("N"));
        var options = Options.Create(new DataStorageOptions { RootDirectory = _tempRoot });
        _repo = new FileSaveRepository(options);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    private RunState FreshRun(ulong seed = 42UL) =>
        RunState.NewSoloRun(
            _catalog,
            rngSeed: seed,
            startNodeId: 0,
            unknownResolutions: System.Collections.Immutable.ImmutableDictionary<int, RoguelikeCardGame.Core.Map.TileKind>.Empty,
            encounterQueueWeak: System.Collections.Immutable.ImmutableArray<string>.Empty,
            encounterQueueStrong: System.Collections.Immutable.ImmutableArray<string>.Empty,
            encounterQueueElite: System.Collections.Immutable.ImmutableArray<string>.Empty,
            encounterQueueBoss: System.Collections.Immutable.ImmutableArray<string>.Empty,
            nowUtc: FixedNow);

    [Fact]
    public async Task Save_CreatesFileUnderSavesSubdir()
    {
        await _repo.SaveAsync("player-001", FreshRun(), CancellationToken.None);
        var expected = Path.Combine(_tempRoot, "saves", "player-001.json");
        Assert.True(File.Exists(expected));
    }

    [Fact]
    public async Task TryLoad_AfterSave_ReturnsEquivalentState()
    {
        var original = FreshRun(seed: 777UL);
        await _repo.SaveAsync("player-002", original, CancellationToken.None);

        var restored = await _repo.TryLoadAsync("player-002", CancellationToken.None);
        Assert.NotNull(restored);
        Assert.Equal(original.RngSeed, restored!.RngSeed);
        Assert.Equal(original.MaxHp, restored.MaxHp);
        Assert.Equal(original.Deck.AsEnumerable(), restored.Deck.AsEnumerable());
    }

    [Fact]
    public async Task TryLoad_MissingAccount_ReturnsNull()
    {
        Assert.Null(await _repo.TryLoadAsync("never-saved", CancellationToken.None));
    }

    [Fact]
    public async Task Save_OverwritesExistingFile()
    {
        await _repo.SaveAsync("p", FreshRun(seed: 1UL), CancellationToken.None);
        await _repo.SaveAsync("p", FreshRun(seed: 2UL), CancellationToken.None);

        var state = await _repo.TryLoadAsync("p", CancellationToken.None);
        Assert.NotNull(state);
        Assert.Equal(2UL, state!.RngSeed);
    }

    [Fact]
    public async Task Delete_ExistingAccount_RemovesFile()
    {
        await _repo.SaveAsync("to-delete", FreshRun(seed: 1), CancellationToken.None);
        await _repo.SaveAsync("to-delete", FreshRun(seed: 2), CancellationToken.None);
        var path = Path.Combine(_tempRoot, "saves", "to-delete.json");
        var backup = path + ".bak";
        Assert.True(File.Exists(path));
        Assert.True(File.Exists(backup));

        await _repo.DeleteAsync("to-delete", CancellationToken.None);

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(backup));
        Assert.Null(await _repo.TryLoadAsync("to-delete", CancellationToken.None));
    }

    [Fact]
    public async Task Delete_MissingAccount_IsNoOp()
    {
        await _repo.DeleteAsync("never-existed", CancellationToken.None);
    }

    [Fact]
    public async Task Save_DoesNotLeaveTmpFile()
    {
        await _repo.SaveAsync("tidy", FreshRun(), CancellationToken.None);
        var saves = Path.Combine(_tempRoot, "saves");
        Assert.Empty(Directory.GetFiles(saves, "tidy.json.tmp.*"));
    }

    [Fact]
    public async Task Save_InvalidState_ThrowsAndPreservesExistingSave()
    {
        await _repo.SaveAsync("valid", FreshRun(seed: 10), CancellationToken.None);
        var invalid = FreshRun(seed: 20) with { PotionSlotCount = 2 };

        await Assert.ThrowsAsync<RunStateSerializerException>(() =>
            _repo.SaveAsync("valid", invalid, CancellationToken.None));

        var loaded = await _repo.TryLoadAsync("valid", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(10UL, loaded!.RngSeed);
    }

    [Fact]
    public async Task TryLoad_CorruptPrimary_RecoversValidBackupAndHealsPrimary()
    {
        await _repo.SaveAsync("recover", FreshRun(seed: 100), CancellationToken.None);
        await _repo.SaveAsync("recover", FreshRun(seed: 200), CancellationToken.None);
        var primary = Path.Combine(_tempRoot, "saves", "recover.json");
        await File.WriteAllTextAsync(primary, "{ corrupt", CancellationToken.None);

        var recovered = await _repo.TryLoadAsync("recover", CancellationToken.None);

        Assert.NotNull(recovered);
        Assert.Equal(100UL, recovered!.RngSeed);
        var healedJson = await File.ReadAllTextAsync(primary, CancellationToken.None);
        Assert.Equal(100UL, RunStateSerializer.Deserialize(healedJson).RngSeed);
    }

    [Fact]
    public async Task TryLoad_MissingPrimary_RecoversBackup()
    {
        await _repo.SaveAsync("recover-missing", FreshRun(seed: 1), CancellationToken.None);
        await _repo.SaveAsync("recover-missing", FreshRun(seed: 2), CancellationToken.None);
        var primary = Path.Combine(_tempRoot, "saves", "recover-missing.json");
        File.Delete(primary);

        var recovered = await _repo.TryLoadAsync("recover-missing", CancellationToken.None);

        Assert.NotNull(recovered);
        Assert.Equal(1UL, recovered!.RngSeed);
        Assert.True(File.Exists(primary));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../escape")]
    [InlineData("has/slash")]
    [InlineData("has\\backslash")]
    public async Task InvalidAccountId_Throws(string bad)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _repo.SaveAsync(bad, FreshRun(), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _repo.TryLoadAsync(bad, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _repo.DeleteAsync(bad, CancellationToken.None));
    }

    [Fact]
    public async Task TryLoad_UnsupportedSave_ThrowsInsteadOfLookingLikeNoSave()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "saves"));
        var v1 = "{\"schemaVersion\":1,\"currentAct\":1,\"currentTileIndex\":0,\"currentHp\":80,\"maxHp\":80,\"gold\":99,\"deck\":[],\"relics\":[],\"potions\":[],\"playSeconds\":0,\"rngSeed\":0,\"savedAtUtc\":\"2026-04-21T00:00:00+00:00\",\"progress\":\"InProgress\"}";
        await File.WriteAllTextAsync(Path.Combine(_tempRoot, "saves", "v1user.json"), v1);
        await Assert.ThrowsAsync<FileSaveRepositoryException>(() =>
            _repo.TryLoadAsync("v1user", CancellationToken.None));
    }

    [Fact]
    public async Task TryLoad_WhenPrimaryAndBackupAreCorrupt_Throws()
    {
        await _repo.SaveAsync("both-corrupt", FreshRun(seed: 1), CancellationToken.None);
        await _repo.SaveAsync("both-corrupt", FreshRun(seed: 2), CancellationToken.None);
        var primary = Path.Combine(_tempRoot, "saves", "both-corrupt.json");
        await File.WriteAllTextAsync(primary, "{ corrupt");
        await File.WriteAllTextAsync(primary + ".bak", "{ also-corrupt");

        await Assert.ThrowsAsync<FileSaveRepositoryException>(() =>
            _repo.TryLoadAsync("both-corrupt", CancellationToken.None));
    }
}
