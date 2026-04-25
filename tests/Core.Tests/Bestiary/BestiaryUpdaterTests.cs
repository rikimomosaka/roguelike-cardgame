using System.Collections.Immutable;
using RoguelikeCardGame.Core.Bestiary;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.History;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Bestiary;

public class BestiaryUpdaterTests
{
    private static RunHistoryRecord MakeRecord(
        string[] cards, string[] relics, string[] potions, string[] enemies)
        => new RunHistoryRecord(
            SchemaVersion: RunHistoryRecord.CurrentSchemaVersion,
            AccountId: "a", RunId: "r", Outcome: RunProgress.Cleared,
            ActReached: 1, NodesVisited: 0, PlaySeconds: 0L,
            CharacterId: "default", FinalHp: 0, FinalMaxHp: 0, FinalGold: 0,
            FinalDeck: ImmutableArray<CardInstance>.Empty,
            FinalRelics: ImmutableArray<string>.Empty,
            EndedAtUtc: System.DateTimeOffset.UnixEpoch,
            SeenCardBaseIds: cards.ToImmutableArray(),
            AcquiredRelicIds: relics.ToImmutableArray(),
            AcquiredPotionIds: potions.ToImmutableArray(),
            EncounteredEnemyIds: enemies.ToImmutableArray(),
            JourneyLog: ImmutableArray<JourneyEntry>.Empty);

    [Fact]
    public void Merge_EmptyPlusRecord_AddsAllCategories()
    {
        var rec = MakeRecord(new[] { "strike" }, new[] { "bb" }, new[] { "fp" }, new[] { "jw" });
        var merged = BestiaryUpdater.Merge(BestiaryState.Empty, rec);
        Assert.Contains("strike", merged.DiscoveredCardBaseIds);
        Assert.Contains("bb", merged.DiscoveredRelicIds);
        Assert.Contains("fp", merged.DiscoveredPotionIds);
        Assert.Contains("jw", merged.EncounteredEnemyIds);
    }

    [Fact]
    public void Merge_Idempotent()
    {
        var rec = MakeRecord(new[] { "strike" }, new[] { "bb" }, new[] { "fp" }, new[] { "jw" });
        var once = BestiaryUpdater.Merge(BestiaryState.Empty, rec);
        var twice = BestiaryUpdater.Merge(once, rec);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void Merge_PreservesCurrent()
    {
        var start = BestiaryState.Empty with
        {
            DiscoveredCardBaseIds = ImmutableHashSet.Create("defend"),
        };
        var rec = MakeRecord(new[] { "strike" }, System.Array.Empty<string>(), System.Array.Empty<string>(), System.Array.Empty<string>());
        var merged = BestiaryUpdater.Merge(start, rec);
        Assert.Contains("defend", merged.DiscoveredCardBaseIds);
        Assert.Contains("strike", merged.DiscoveredCardBaseIds);
    }
}
