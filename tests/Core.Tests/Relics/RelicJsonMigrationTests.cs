using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Relics;

public class RelicJsonMigrationTests
{
    private static string RelicDir =>
        Path.Combine(FindRepoRoot(), "src", "Core", "Data", "Relics");

    private static string PotionDir =>
        Path.Combine(FindRepoRoot(), "src", "Core", "Data", "Potions");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null &&
               !File.Exists(Path.Combine(dir.FullName, "RoguelikeCardGame.sln")) &&
               !File.Exists(Path.Combine(dir.FullName, "RoguelikeCardGame.slnx")))
            dir = dir.Parent;
        if (dir is null) throw new InvalidOperationException("repo root not found");
        return dir.FullName;
    }

    public static IEnumerable<object[]> RelicFiles()
        => Directory.EnumerateFiles(RelicDir, "*.json").Select(f => new object[] { f });

    public static IEnumerable<object[]> PotionFiles()
        => Directory.EnumerateFiles(PotionDir, "*.json").Select(f => new object[] { f });

    [Theory]
    [MemberData(nameof(RelicFiles))]
    public void All_relics_have_implemented_field(string path)
    {
        var content = File.ReadAllText(path);
        Assert.Contains("\"implemented\"", content);
    }

    [Theory]
    [MemberData(nameof(PotionFiles))]
    public void Potion_JSON_no_legacy_usable_flags(string path)
    {
        var content = File.ReadAllText(path);
        var legacy = new[] { "\"usableInBattle\"", "\"usableOutOfBattle\"" };
        foreach (var key in legacy)
            Assert.False(content.Contains(key),
                $"{Path.GetFileName(path)} contains legacy field {key}");
    }

    [Theory]
    [MemberData(nameof(PotionFiles))]
    public void Potion_JSON_no_legacy_action_names(string path)
    {
        var content = File.ReadAllText(path);
        var legacyActions = new[] { "\"applyPoison\"", "\"gainStrength\"", "\"drawCards\"" };
        foreach (var name in legacyActions)
            Assert.False(content.Contains(name),
                $"{Path.GetFileName(path)} contains legacy action {name}");
    }
}
