using System.Collections.Immutable;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.History;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.History;

public class RunHistoryBuilderTests
{
    [Fact]
    public void From_CopiesBasicFields()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat) with
        {
            CurrentAct = 2,
            CurrentHp = 30,
            MaxHp = 80,
            Gold = 123,
            PlaySeconds = 456,
        };
        var rec = RunHistoryBuilder.From("acc_abc", s, nodesVisited: 7, RunProgress.GameOver);
        Assert.Equal(2, rec.SchemaVersion);
        Assert.Equal("acc_abc", rec.AccountId);
        Assert.Equal(s.RunId, rec.RunId);
        Assert.Equal(RunProgress.GameOver, rec.Outcome);
        Assert.Equal(2, rec.ActReached);
        Assert.Equal(30, rec.FinalHp);
        Assert.Equal(80, rec.FinalMaxHp);
        Assert.Equal(123, rec.FinalGold);
        Assert.Equal(456, rec.PlaySeconds);
        Assert.Equal(7, rec.NodesVisited);
    }

    [Fact]
    public void From_CopiesBestiaryFields()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var state = TestRunStates.FreshDefault(cat) with
        {
            SeenCardBaseIds = ImmutableArray.Create("strike", "defend"),
            AcquiredRelicIds = ImmutableArray.Create("burning_blood"),
            AcquiredPotionIds = ImmutableArray.Create("fire_potion"),
            EncounteredEnemyIds = ImmutableArray.Create("jaw_worm"),
        };
        var rec = RunHistoryBuilder.From("acct", state, nodesVisited: 3, outcome: RunProgress.Cleared);
        Assert.Equal(new[] { "strike", "defend" }, rec.SeenCardBaseIds.ToArray());
        Assert.Equal(new[] { "burning_blood" }, rec.AcquiredRelicIds.ToArray());
        Assert.Equal(new[] { "fire_potion" }, rec.AcquiredPotionIds.ToArray());
        Assert.Equal(new[] { "jaw_worm" }, rec.EncounteredEnemyIds.ToArray());
        Assert.Equal(RunHistoryRecord.CurrentSchemaVersion, rec.SchemaVersion);
    }
}
