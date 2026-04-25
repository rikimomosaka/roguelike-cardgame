using System.Collections.Immutable;
using RoguelikeCardGame.Core.History;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.History;

public class RunHistoryRecordTests
{
    [Fact]
    public void CurrentSchemaVersion_Is3()
    {
        Assert.Equal(3, RunHistoryRecord.CurrentSchemaVersion);
    }

    [Fact]
    public void Record_HasBestiaryFields()
    {
        var rec = new RunHistoryRecord(
            SchemaVersion: RunHistoryRecord.CurrentSchemaVersion,
            AccountId: "a",
            RunId: "r",
            Outcome: RoguelikeCardGame.Core.Run.RunProgress.Cleared,
            ActReached: 1,
            NodesVisited: 0,
            PlaySeconds: 0L,
            CharacterId: "default",
            FinalHp: 80,
            FinalMaxHp: 80,
            FinalGold: 99,
            FinalDeck: ImmutableArray<RoguelikeCardGame.Core.Cards.CardInstance>.Empty,
            FinalRelics: ImmutableArray<string>.Empty,
            EndedAtUtc: System.DateTimeOffset.UnixEpoch,
            SeenCardBaseIds: ImmutableArray.Create("strike"),
            AcquiredRelicIds: ImmutableArray<string>.Empty,
            AcquiredPotionIds: ImmutableArray<string>.Empty,
            EncounteredEnemyIds: ImmutableArray<string>.Empty,
            JourneyLog: ImmutableArray<JourneyEntry>.Empty);
        Assert.Contains("strike", rec.SeenCardBaseIds);
    }
}
