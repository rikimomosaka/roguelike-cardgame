using System.Collections.Immutable;
using RoguelikeCardGame.Core.Bestiary;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Core.Tests;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Bestiary;

public class BestiaryTrackerTests
{
    private static RunState Fresh() => TestRunStates.FreshDefault(EmbeddedDataLoader.LoadCatalog());

    [Fact]
    public void NoteCardsSeen_AddsIds_Deduplicated()
    {
        var s = Fresh();
        s = BestiaryTracker.NoteCardsSeen(s, new[] { "strike", "defend", "strike" });
        Assert.Contains("strike", s.SeenCardBaseIds);
        Assert.Contains("defend", s.SeenCardBaseIds);
        // Each should appear only once
        Assert.Equal(s.SeenCardBaseIds.Length, s.SeenCardBaseIds.Distinct().Count());
    }

    [Fact]
    public void NoteCardsSeen_Null_NoOp()
    {
        var s = Fresh();
        var after = BestiaryTracker.NoteCardsSeen(s, null);
        Assert.Same(s, after);
    }

    [Fact]
    public void NoteCardsSeen_Empty_NoOp()
    {
        var s = Fresh();
        var after = BestiaryTracker.NoteCardsSeen(s, System.Array.Empty<string>());
        Assert.Same(s, after);
    }

    [Fact]
    public void NoteCardsSeen_Idempotent()
    {
        var s = Fresh();
        s = BestiaryTracker.NoteCardsSeen(s, new[] { "strike" });
        var again = BestiaryTracker.NoteCardsSeen(s, new[] { "strike" });
        Assert.Same(s, again);
    }

    [Fact]
    public void NoteRelicsAcquired_AddsAndDedupes()
    {
        var s = Fresh();
        s = BestiaryTracker.NoteRelicsAcquired(s, new[] { "burning_blood" });
        s = BestiaryTracker.NoteRelicsAcquired(s, new[] { "burning_blood", "anchor" });
        Assert.Contains("anchor", s.AcquiredRelicIds);
        Assert.Contains("burning_blood", s.AcquiredRelicIds);
        Assert.Equal(2, s.AcquiredRelicIds.Length);
    }

    [Fact]
    public void NotePotionsAcquired_AddsAndDedupes()
    {
        var s = Fresh();
        s = BestiaryTracker.NotePotionsAcquired(s, new[] { "fire_potion", "fire_potion" });
        Assert.Equal(new[] { "fire_potion" }, s.AcquiredPotionIds.ToArray());
    }

    [Fact]
    public void NoteEnemiesEncountered_AddsAndDedupes()
    {
        var s = Fresh();
        s = BestiaryTracker.NoteEnemiesEncountered(s, new[] { "jaw_worm", "cultist" });
        s = BestiaryTracker.NoteEnemiesEncountered(s, new[] { "jaw_worm" });
        Assert.Contains("cultist", s.EncounteredEnemyIds);
        Assert.Contains("jaw_worm", s.EncounteredEnemyIds);
        Assert.Equal(2, s.EncounteredEnemyIds.Length);
    }

    [Fact]
    public void NoteCardsSeen_NullOrEmptyStrings_Skipped()
    {
        // SeenCardBaseIds を空にリセットして、null/空文字スキップのみを検証する
        var s = Fresh() with { SeenCardBaseIds = ImmutableArray<string>.Empty };
        s = BestiaryTracker.NoteCardsSeen(s, new[] { null!, "", "strike" });
        Assert.Equal(new[] { "strike" }, s.SeenCardBaseIds.ToArray());
    }
}
