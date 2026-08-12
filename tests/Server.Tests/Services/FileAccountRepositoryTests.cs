using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Services;
using RoguelikeCardGame.Server.Services.FileBacked;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Services;

public class FileAccountRepositoryTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly FileAccountRepository _repo;
    private static readonly DateTimeOffset FixedNow = new(2026, 4, 20, 12, 0, 0, TimeSpan.Zero);

    public FileAccountRepositoryTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "rcg-account-tests-" + Guid.NewGuid().ToString("N"));
        var options = Options.Create(new DataStorageOptions { RootDirectory = _tempRoot });
        _repo = new FileAccountRepository(options);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task ExistsAsync_ReturnsFalseForMissingAccount()
    {
        Assert.False(await _repo.ExistsAsync("never", CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_ThenExists_ReturnsTrue()
    {
        await _repo.CreateAsync("alice", FixedNow, CancellationToken.None);
        Assert.True(await _repo.ExistsAsync("alice", CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_Twice_ThrowsAccountAlreadyExists()
    {
        await _repo.CreateAsync("dup", FixedNow, CancellationToken.None);
        await Assert.ThrowsAsync<AccountAlreadyExistsException>(() =>
            _repo.CreateAsync("dup", FixedNow, CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_Missing_ReturnsNull()
    {
        Assert.Null(await _repo.GetAsync("missing", CancellationToken.None));
    }

    [Fact]
    public async Task GetAsync_AfterCreate_ReturnsAccountWithCreatedAt()
    {
        await _repo.CreateAsync("bob", FixedNow, CancellationToken.None);
        var got = await _repo.GetAsync("bob", CancellationToken.None);
        Assert.NotNull(got);
        Assert.Equal("bob", got!.Id);
        Assert.Equal(FixedNow, got.CreatedAt);
    }

    [Fact]
    public async Task Lookup_IsCaseInsensitive_AndReturnsStoredCanonicalId()
    {
        await _repo.CreateAsync("Alice", FixedNow, CancellationToken.None);

        Assert.True(await _repo.ExistsAsync("alice", CancellationToken.None));
        var got = await _repo.GetAsync("ALICE", CancellationToken.None);

        Assert.NotNull(got);
        Assert.Equal("Alice", got!.Id);
    }

    [Fact]
    public async Task CreateAsync_CaseVariant_ThrowsAccountAlreadyExists_OnEveryOs()
    {
        await _repo.CreateAsync("CaseOwner", FixedNow, CancellationToken.None);

        await Assert.ThrowsAsync<AccountAlreadyExistsException>(() =>
            _repo.CreateAsync("caseowner", FixedNow, CancellationToken.None));

        Assert.Single(Directory.GetFiles(Path.Combine(_tempRoot, "accounts"), "*.json"));
    }

    [Fact]
    public async Task ConcurrentCaseVariantCreates_ExactlyOneSucceeds()
    {
        static async Task<Exception?> TryCreateAsync(
            FileAccountRepository repo, string id)
        {
            try
            {
                await repo.CreateAsync(id, FixedNow, CancellationToken.None);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        var outcomes = await Task.WhenAll(
            TryCreateAsync(_repo, "RaceOwner"),
            TryCreateAsync(_repo, "raceowner"));

        Assert.Single(outcomes, outcome => outcome is null);
        Assert.Single(outcomes, outcome => outcome is AccountAlreadyExistsException);
        var stored = await _repo.GetAsync("RACEOWNER", CancellationToken.None);
        Assert.NotNull(stored);
        Assert.True(
            string.Equals(stored!.Id, "RaceOwner", StringComparison.Ordinal)
            || string.Equals(stored.Id, "raceowner", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateAsync_WritesUnderAccountsSubdir()
    {
        await _repo.CreateAsync("carol", FixedNow, CancellationToken.None);
        var expected = Path.Combine(_tempRoot, "accounts", "carol.json");
        Assert.True(File.Exists(expected));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("has/slash")]
    [InlineData("has\\backslash")]
    public async Task InvalidId_Throws(string bad)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _repo.CreateAsync(bad, FixedNow, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _repo.ExistsAsync(bad, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _repo.GetAsync(bad, CancellationToken.None));
    }
}
