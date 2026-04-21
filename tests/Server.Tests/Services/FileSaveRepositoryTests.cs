using System;
using System.IO;
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
        Assert.Equal(original.Deck, restored.Deck);
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
        await _repo.SaveAsync("to-delete", FreshRun(), CancellationToken.None);
        var path = Path.Combine(_tempRoot, "saves", "to-delete.json");
        Assert.True(File.Exists(path));

        await _repo.DeleteAsync("to-delete", CancellationToken.None);

        Assert.False(File.Exists(path));
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
        var tmp = Path.Combine(_tempRoot, "saves", "tidy.json.tmp");
        Assert.False(File.Exists(tmp));
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
}
