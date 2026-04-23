using System.Collections.Immutable;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.History;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Dtos;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Dtos;

public class RunSnapshotDtoMapperBestiaryTests
{
    [Fact]
    public void ToResultDto_MapsBestiaryFields()
    {
        var rec = new RunHistoryRecord(
            SchemaVersion: RunHistoryRecord.CurrentSchemaVersion,
            AccountId: "a", RunId: "r", Outcome: RunProgress.Cleared,
            ActReached: 3, NodesVisited: 15, PlaySeconds: 1000,
            CharacterId: "default", FinalHp: 40, FinalMaxHp: 80, FinalGold: 200,
            FinalDeck: ImmutableArray<CardInstance>.Empty,
            FinalRelics: ImmutableArray<string>.Empty,
            EndedAtUtc: System.DateTimeOffset.UnixEpoch,
            SeenCardBaseIds: ImmutableArray.Create("strike", "defend"),
            AcquiredRelicIds: ImmutableArray.Create("burning_blood"),
            AcquiredPotionIds: ImmutableArray.Create("fire_potion"),
            EncounteredEnemyIds: ImmutableArray.Create("jaw_worm"));
        var dto = RunSnapshotDtoMapper.ToResultDto(rec);
        Assert.Equal(new[] { "strike", "defend" }, dto.SeenCardBaseIds);
        Assert.Equal(new[] { "burning_blood" }, dto.AcquiredRelicIds);
        Assert.Equal(new[] { "fire_potion" }, dto.AcquiredPotionIds);
        Assert.Equal(new[] { "jaw_worm" }, dto.EncounteredEnemyIds);
    }
}
