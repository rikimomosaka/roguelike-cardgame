using System;
using RoguelikeCardGame.Core.Data;
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
        var state = RunState.NewSoloRun(catalog, rngSeed: 42UL, nowUtc: FixedNow);

        Assert.Equal(1, state.SchemaVersion);
        Assert.Equal(1, state.CurrentAct);
        Assert.Equal(0, state.CurrentTileIndex);
        Assert.Equal(80, state.CurrentHp);
        Assert.Equal(80, state.MaxHp);
        Assert.Equal(99, state.Gold);
        Assert.Equal(10, state.Deck.Length);
        Assert.Equal(5, Array.FindAll(state.Deck, id => id == "strike").Length);
        Assert.Equal(5, Array.FindAll(state.Deck, id => id == "defend").Length);
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
            cards: System.Array.Empty<string>(),
            relics: System.Array.Empty<string>(),
            potions: System.Array.Empty<string>(),
            enemies: System.Array.Empty<string>());

        var ex = Assert.Throws<InvalidOperationException>(
            () => RunState.NewSoloRun(emptyCatalog, rngSeed: 0UL, nowUtc: FixedNow));
        Assert.Contains("strike", ex.Message);
    }
}
