using System;
using System.IO;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Services;
using Xunit;

namespace RoguelikeCardGame.Server.Tests;

public class SaveRepositoryTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly DataCatalog _catalog = EmbeddedDataLoader.LoadCatalog();
    private static readonly DateTimeOffset FixedNow = new(2026, 4, 20, 12, 0, 0, TimeSpan.Zero);

    public SaveRepositoryTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "rcg-save-tests-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    private RunState FreshRun(ulong seed = 42UL) =>
        RunState.NewSoloRun(_catalog, rngSeed: seed, nowUtc: FixedNow);

    [Fact]
    public void Save_CreatesFileForAccountId()
    {
        var repo = new SaveRepository(_tempRoot);
        repo.Save("player-001", FreshRun());

        var expectedPath = Path.Combine(_tempRoot, "player-001.json");
        Assert.True(File.Exists(expectedPath));
    }

    [Fact]
    public void TryLoad_AfterSave_ReturnsEquivalentState()
    {
        var repo = new SaveRepository(_tempRoot);
        var original = FreshRun(seed: 777UL);
        repo.Save("player-002", original);

        Assert.True(repo.TryLoad("player-002", out var restored));
        Assert.NotNull(restored);
        Assert.Equal(original.RngSeed, restored!.RngSeed);
        Assert.Equal(original.MaxHp, restored.MaxHp);
        Assert.Equal(original.Deck, restored.Deck);
    }

    [Fact]
    public void TryLoad_MissingAccount_ReturnsFalseAndNull()
    {
        var repo = new SaveRepository(_tempRoot);
        Assert.False(repo.TryLoad("never-saved", out var state));
        Assert.Null(state);
    }

    [Fact]
    public void Save_OverwritesExistingFile()
    {
        var repo = new SaveRepository(_tempRoot);
        repo.Save("p", FreshRun(seed: 1UL));
        repo.Save("p", FreshRun(seed: 2UL));

        Assert.True(repo.TryLoad("p", out var state));
        Assert.Equal(2UL, state!.RngSeed);
    }
}
