using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Services;
using RoguelikeCardGame.Server.Services.FileBacked;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.FileBacked;

public class FileHistoryRepositoryMigrationTests : IDisposable
{
    private readonly string _root;
    private readonly FileHistoryRepository _repo;

    public FileHistoryRepositoryMigrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "roguelike-history-mig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var opts = Options.Create(new DataStorageOptions { RootDirectory = _root });
        _repo = new FileHistoryRepository(opts);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public async Task Load_V1_File_FillsEmptyBestiarySets()
    {
        var accountDir = Path.Combine(_root, "history", "acct");
        Directory.CreateDirectory(accountDir);
        var v1 = """
        {
          "schemaVersion": 1,
          "accountId": "acct",
          "runId": "r1",
          "outcome": "Cleared",
          "actReached": 3,
          "nodesVisited": 15,
          "playSeconds": 1200,
          "characterId": "default",
          "finalHp": 40,
          "finalMaxHp": 80,
          "finalGold": 150,
          "finalDeck": [],
          "finalRelics": [],
          "endedAtUtc": "2025-01-01T00:00:00+00:00"
        }
        """;
        await File.WriteAllTextAsync(Path.Combine(accountDir, "20250101T000000000Z_r1.json"), v1, new UTF8Encoding(false));

        var list = await _repo.ListAsync("acct", default);
        Assert.Single(list);
        var rec = list[0];
        Assert.Empty(rec.SeenCardBaseIds);
        Assert.Empty(rec.AcquiredRelicIds);
        Assert.Empty(rec.AcquiredPotionIds);
        Assert.Empty(rec.EncounteredEnemyIds);
        Assert.False(rec.SeenCardBaseIds.IsDefault);
    }
}
