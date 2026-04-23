using System.Collections.Immutable;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class RunStateBestiaryFieldsTests
{
    [Fact]
    public void CurrentSchemaVersion_Is6()
    {
        Assert.Equal(6, RunState.CurrentSchemaVersion);
    }

    [Fact]
    public void NewSoloRun_InitializesBestiarySets_NonDefault_Empty()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var state = TestRunStates.FreshDefault(cat);
        Assert.False(state.SeenCardBaseIds.IsDefault);
        Assert.False(state.AcquiredRelicIds.IsDefault);
        Assert.False(state.AcquiredPotionIds.IsDefault);
        Assert.False(state.EncounteredEnemyIds.IsDefault);
        Assert.Empty(state.AcquiredRelicIds);
        Assert.Empty(state.AcquiredPotionIds);
        Assert.Empty(state.EncounteredEnemyIds);
        Assert.NotEmpty(state.SeenCardBaseIds); // 初期デッキのカード ID がシードされる
    }
}
