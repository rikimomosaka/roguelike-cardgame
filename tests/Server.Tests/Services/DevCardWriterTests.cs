using System;
using System.IO;
using RoguelikeCardGame.Server.Services;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Services;

/// <summary>
/// DevCardWriter (Phase 10.5.J) — override file の read/write/delete と
/// base + backup 書き込みを集約するヘルパのテスト。
/// </summary>
public class DevCardWriterTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly DevCardWriter _writer;

    public DevCardWriterTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "writer-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _writer = new DevCardWriter(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public void WriteOverride_creates_file_with_contents()
    {
        const string json = """{ "id": "strike", "versions": [] }""";
        _writer.WriteOverride("strike", json);

        var path = Path.Combine(_tempRoot, "cards", "strike.json");
        Assert.True(File.Exists(path));
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Fact]
    public void ReadOverride_returns_null_when_missing()
    {
        Assert.Null(_writer.ReadOverride("nonexistent"));
    }

    [Fact]
    public void ReadOverride_returns_contents_when_present()
    {
        _writer.WriteOverride("strike", "{\"id\":\"strike\"}");
        Assert.Equal("{\"id\":\"strike\"}", _writer.ReadOverride("strike"));
    }

    [Fact]
    public void DeleteOverride_removes_file()
    {
        _writer.WriteOverride("strike", "{}");
        _writer.DeleteOverride("strike");
        var path = Path.Combine(_tempRoot, "cards", "strike.json");
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void DeleteOverride_missing_is_noop()
    {
        // 例外を投げない
        var ex = Record.Exception(() => _writer.DeleteOverride("nonexistent"));
        Assert.Null(ex);
    }

    [Fact]
    public void WriteBaseWithBackup_throws_without_baseCardsDir()
    {
        Assert.Throws<InvalidOperationException>(() => _writer.WriteBaseWithBackup("x", "{}"));
    }

    [Fact]
    public void WriteBaseWithBackup_writes_base_and_creates_backup()
    {
        var baseDir = Path.Combine(_tempRoot, "base");
        var backupDir = Path.Combine(_tempRoot, "backups");
        Directory.CreateDirectory(baseDir);
        var basePath = Path.Combine(baseDir, "strike.json");
        File.WriteAllText(basePath, """{ "id": "strike", "old": true }""");

        var w = new DevCardWriter(_tempRoot, baseDir, backupDir);
        w.WriteBaseWithBackup("strike", """{ "id": "strike", "new": true }""");

        Assert.Equal("""{ "id": "strike", "new": true }""", File.ReadAllText(basePath));
        Assert.True(Directory.Exists(Path.Combine(backupDir, "cards")));
        var backups = Directory.GetFiles(Path.Combine(backupDir, "cards"), "strike-*.json");
        Assert.Single(backups);
        Assert.Contains("\"old\": true", File.ReadAllText(backups[0]));
    }

    [Fact]
    public void ReadBase_returns_contents_when_present()
    {
        var baseDir = Path.Combine(_tempRoot, "base");
        Directory.CreateDirectory(baseDir);
        File.WriteAllText(Path.Combine(baseDir, "strike.json"), "{\"id\":\"strike\"}");

        var w = new DevCardWriter(_tempRoot, baseDir);
        Assert.Equal("{\"id\":\"strike\"}", w.ReadBase("strike"));
    }

    [Fact]
    public void ReadBase_returns_null_without_baseCardsDir()
    {
        Assert.Null(_writer.ReadBase("strike"));
    }
}
