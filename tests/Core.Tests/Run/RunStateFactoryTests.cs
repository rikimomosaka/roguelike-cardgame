using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class RunStateFactoryTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 4, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NewSoloRun_InitialValues()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var state = RunState.NewSoloRun(
            catalog,
            rngSeed: 42UL,
            startNodeId: 0,
            unknownResolutions: ImmutableDictionary<int, TileKind>.Empty,
            nowUtc: FixedNow);

        Assert.Equal(2, state.SchemaVersion);
        Assert.Equal(1, state.CurrentAct);
        Assert.Equal(0, state.CurrentNodeId);
        Assert.Equal(new[] { 0 }, state.VisitedNodeIds.ToArray());
        Assert.Empty(state.UnknownResolutions);
        Assert.Equal(80, state.CurrentHp);
        Assert.Equal(80, state.MaxHp);
        Assert.Equal(99, state.Gold);
        Assert.Equal(10, state.Deck.Count);
        Assert.Equal(5, state.Deck.Count(id => id == "strike"));
        Assert.Equal(5, state.Deck.Count(id => id == "defend"));
        Assert.Empty(state.Relics);
        Assert.Empty(state.Potions);
        Assert.Equal(0L, state.PlaySeconds);
        Assert.Equal(42UL, state.RngSeed);
        Assert.Equal(FixedNow, state.SavedAtUtc);
        Assert.Equal(RunProgress.InProgress, state.Progress);
    }

    [Fact]
    public void NewSoloRun_ThrowsWhenStarterCardMissingFromCatalog()
    {
        var emptyCatalog = DataCatalog.LoadFromStrings(
            cards: Array.Empty<string>(),
            relics: Array.Empty<string>(),
            potions: Array.Empty<string>(),
            enemies: Array.Empty<string>(),
            encounters: Array.Empty<string>(),
            rewardTables: Array.Empty<string>(),
            characters: Array.Empty<string>());

        var ex = Assert.Throws<InvalidOperationException>(
            () => RunState.NewSoloRun(
                emptyCatalog,
                rngSeed: 0UL,
                startNodeId: 0,
                unknownResolutions: ImmutableDictionary<int, TileKind>.Empty,
                nowUtc: FixedNow));
        Assert.Contains("strike", ex.Message);
    }
}
