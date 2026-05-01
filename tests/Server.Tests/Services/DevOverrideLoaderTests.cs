using System;
using System.IO;
using RoguelikeCardGame.Server.Services;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Services;

public class DevOverrideLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public DevOverrideLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dev-override-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "cards"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void NonExistent_dir_returns_empty_dict()
    {
        var nonexistent = Path.Combine(Path.GetTempPath(), "no-such-dir-" + Guid.NewGuid().ToString("N"));
        var result = DevOverrideLoader.LoadCards(nonexistent);
        Assert.Empty(result);
    }

    [Fact]
    public void Reads_card_override_jsons()
    {
        var path = Path.Combine(_tempDir, "cards", "strike.json");
        File.WriteAllText(path, """{ "id": "strike", "activeVersion": "v2", "versions": [] }""");

        var result = DevOverrideLoader.LoadCards(_tempDir);

        Assert.Single(result);
        Assert.True(result.ContainsKey("strike"));
    }

    [Fact]
    public void Skips_jsons_without_id()
    {
        var noId = Path.Combine(_tempDir, "cards", "broken.json");
        File.WriteAllText(noId, """{ "name": "no-id" }""");

        var ok = Path.Combine(_tempDir, "cards", "ok.json");
        File.WriteAllText(ok, """{ "id": "ok", "versions": [] }""");

        var result = DevOverrideLoader.LoadCards(_tempDir);

        Assert.Single(result);
        Assert.True(result.ContainsKey("ok"));
    }

    [Fact]
    public void Skips_invalid_json_files()
    {
        var bad = Path.Combine(_tempDir, "cards", "bad.json");
        File.WriteAllText(bad, "not json {{{");

        var ok = Path.Combine(_tempDir, "cards", "ok.json");
        File.WriteAllText(ok, """{ "id": "ok" }""");

        var result = DevOverrideLoader.LoadCards(_tempDir);

        Assert.Single(result);
        Assert.True(result.ContainsKey("ok"));
    }
}
