using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Definitions;

public class EnemyJsonMigrationTests
{
    private static string EnemyDir =>
        Path.Combine(FindRepoRoot(), "src", "Core", "Data", "Enemies");

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

    public static IEnumerable<object[]> EnemyFiles()
        => Directory.EnumerateFiles(EnemyDir, "*.json").Select(f => new object[] { f });

    [Theory]
    [MemberData(nameof(EnemyFiles))]
    public void No_legacy_field_names(string path)
    {
        var content = File.ReadAllText(path);
        // Legacy format had these as object properties (field names), not as action types
        // We check for the pattern "fieldName": to avoid matching "action": "buff" in the new format
        var legacy = new[] { "\"hpMin\":", "\"hpMax\":", "\"damageMin\":", "\"damageMax\":",
                             "\"hits\":", "\"blockMin\":", "\"blockMax\":", "\"buff\":",
                             "\"amountMin\":", "\"amountMax\":" };
        foreach (var key in legacy)
            Assert.False(content.Contains(key), $"{Path.GetFileName(path)} contains legacy key {key}");
    }

    [Theory]
    [MemberData(nameof(EnemyFiles))]
    public void No_unsupported_buff_names(string path)
    {
        var content = File.ReadAllText(path);
        // Unsupported buff names that should not appear in migrated JSONs
        var unsupported = new[] { "\"ritual\"", "\"enrage\"", "\"curl_up\"", "\"activate\"", "\"split\"" };
        foreach (var pattern in unsupported)
            Assert.False(content.Contains(pattern), $"{Path.GetFileName(path)} contains unsupported buff {pattern}");
    }
}
