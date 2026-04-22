using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class RunStateIdentityTests
{
    [Fact]
    public void NewSoloRun_GeneratesRunId()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat);
        Assert.False(string.IsNullOrEmpty(s.RunId));
    }

    [Fact]
    public void NewSoloRun_GeneratesDistinctRunIds()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var a = TestRunStates.FreshDefault(cat);
        var b = TestRunStates.FreshDefault(cat);
        Assert.NotEqual(a.RunId, b.RunId);
    }

    [Fact]
    public void NewSoloRun_VisitedNodeIdsEmpty()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat);
        Assert.Empty(s.VisitedNodeIds);
    }

    [Fact]
    public void NewSoloRun_ActiveActStartRelicChoiceNull()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat);
        Assert.Null(s.ActiveActStartRelicChoice);
    }

    [Fact]
    public void Validate_AllowsStartUnvisitedWhenChoiceActive()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat) with
        {
            ActiveActStartRelicChoice = new ActStartRelicChoice(
                System.Collections.Immutable.ImmutableArray.Create("a", "b", "c")),
        };
        Assert.Null(s.Validate());
    }
}
