using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using RoguelikeCardGame.Core.Settings;
using RoguelikeCardGame.Server.Services;
using RoguelikeCardGame.Server.Services.FileBacked;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Services;

public class FileAudioSettingsRepositoryTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly FileAudioSettingsRepository _repo;

    public FileAudioSettingsRepositoryTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "rcg-audio-tests-" + Guid.NewGuid().ToString("N"));
        var options = Options.Create(new DataStorageOptions { RootDirectory = _tempRoot });
        _repo = new FileAudioSettingsRepository(options);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task GetOrDefault_NoFile_ReturnsDefault()
    {
        var got = await _repo.GetOrDefaultAsync("new-player", CancellationToken.None);
        Assert.Equal(AudioSettings.Default, got);
    }

    [Fact]
    public async Task Upsert_ThenGet_ReturnsPersistedValue()
    {
        var custom = AudioSettings.Create(master: 10, bgm: 20, se: 30, ambient: 40);
        await _repo.UpsertAsync("alice", custom, CancellationToken.None);
        var got = await _repo.GetOrDefaultAsync("alice", CancellationToken.None);
        Assert.Equal(custom, got);
    }

    [Fact]
    public async Task Upsert_OverwritesPreviousValue()
    {
        await _repo.UpsertAsync("bob", AudioSettings.Create(1, 1, 1, 1), CancellationToken.None);
        await _repo.UpsertAsync("bob", AudioSettings.Create(99, 99, 99, 99), CancellationToken.None);
        var got = await _repo.GetOrDefaultAsync("bob", CancellationToken.None);
        Assert.Equal(99, got.Master);
    }

    [Fact]
    public async Task Upsert_IsIsolatedPerAccount()
    {
        await _repo.UpsertAsync("a", AudioSettings.Create(10, 10, 10, 10), CancellationToken.None);
        await _repo.UpsertAsync("b", AudioSettings.Create(90, 90, 90, 90), CancellationToken.None);
        var a = await _repo.GetOrDefaultAsync("a", CancellationToken.None);
        var b = await _repo.GetOrDefaultAsync("b", CancellationToken.None);
        Assert.Equal(10, a.Master);
        Assert.Equal(90, b.Master);
    }

    [Fact]
    public async Task Upsert_WritesUnderAudioSettingsSubdir()
    {
        await _repo.UpsertAsync("carol", AudioSettings.Default, CancellationToken.None);
        var expected = Path.Combine(_tempRoot, "audio_settings", "carol.json");
        Assert.True(File.Exists(expected));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("has/slash")]
    public async Task InvalidId_Throws(string bad)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _repo.GetOrDefaultAsync(bad, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _repo.UpsertAsync(bad, AudioSettings.Default, CancellationToken.None));
    }

    [Fact]
    public async Task GetOrDefault_AfterCorruptedFile_FallsBackToDefault()
    {
        var subdir = Path.Combine(_tempRoot, "audio_settings");
        Directory.CreateDirectory(subdir);
        await File.WriteAllTextAsync(Path.Combine(subdir, "corrupt.json"), "not-json");

        // 仕様上は破損ファイル → Default ではなく例外、が将来の堅牢化候補。
        // Phase 2 では破損時も Default を返して運用継続する (ユーザ体験優先)。
        var got = await _repo.GetOrDefaultAsync("corrupt", CancellationToken.None);
        Assert.Equal(AudioSettings.Default, got);
    }
}
