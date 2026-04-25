using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class RunStateSerializerMigrationTests
{
    [Fact]
    public void V4ToV5_FillsRunIdAndDefaults()
    {
        // 最小 v4 JSON。Deck や VisitedNodeIds など既存 v4 スキーマが書けるだけ書く。
        var v4json = BuildMinimalV4Json();
        var s = RunStateSerializer.Deserialize(v4json);
        Assert.Equal(RunState.CurrentSchemaVersion, s.SchemaVersion);
        Assert.False(string.IsNullOrEmpty(s.RunId));
        Assert.Null(s.ActiveActStartRelicChoice);
    }

    [Fact]
    public void V5_To_V6_FillsEmptyBestiarySets()
    {
        var v5Json = """
        {
          "schemaVersion": 5,
          "currentAct": 1,
          "currentNodeId": 0,
          "visitedNodeIds": [0],
          "unknownResolutions": {},
          "characterId": "default",
          "currentHp": 80,
          "maxHp": 80,
          "gold": 99,
          "deck": [],
          "potions": ["", "", ""],
          "potionSlotCount": 3,
          "activeBattle": null,
          "activeReward": null,
          "encounterQueueWeak": [],
          "encounterQueueStrong": [],
          "encounterQueueElite": [],
          "encounterQueueBoss": [],
          "rewardRngState": { "potionChancePercent": 40, "rareChanceBonusPercent": 0 },
          "activeMerchant": null,
          "activeEvent": null,
          "activeRestPending": false,
          "activeRestCompleted": false,
          "relics": [],
          "playSeconds": 0,
          "rngSeed": 42,
          "savedAtUtc": "1970-01-01T00:00:00+00:00",
          "progress": "InProgress",
          "runId": "00000000-0000-0000-0000-000000000000",
          "activeActStartRelicChoice": null,
          "discardUsesSoFar": 0
        }
        """;

        var state = RunStateSerializer.Deserialize(v5Json);
        Assert.Equal(RunState.CurrentSchemaVersion, state.SchemaVersion);
        Assert.Empty(state.SeenCardBaseIds);
        Assert.Empty(state.AcquiredRelicIds);
        Assert.Empty(state.AcquiredPotionIds);
        Assert.Empty(state.EncounteredEnemyIds);
        Assert.False(state.SeenCardBaseIds.IsDefault);
    }

    private static string BuildMinimalV4Json()
    {
        // 作成: 最小 v4 run を NewSoloRun で作り、schemaVersion を 4 に書き換えて serialize。
        var cat = RoguelikeCardGame.Core.Data.EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat);
        var json = RunStateSerializer.Serialize(s);
        // schemaVersion を 4 に書き換える
        json = System.Text.RegularExpressions.Regex.Replace(
            json, "\"schemaVersion\":\\d+", "\"schemaVersion\":4");
        // runId / activeActStartRelicChoice フィールドを削除して v4 体裁に戻す
        json = System.Text.RegularExpressions.Regex.Replace(
            json, ",\"runId\":\"[^\"]*\"", "");
        json = System.Text.RegularExpressions.Regex.Replace(
            json, ",\"activeActStartRelicChoice\":(null|\\{[^}]*\\})", "");
        // Phase 8 bestiary fields を削除して v4 体裁を保つ
        json = System.Text.RegularExpressions.Regex.Replace(json, ",\"seenCardBaseIds\":\\[[^\\]]*\\]", "");
        json = System.Text.RegularExpressions.Regex.Replace(json, ",\"acquiredRelicIds\":\\[[^\\]]*\\]", "");
        json = System.Text.RegularExpressions.Regex.Replace(json, ",\"acquiredPotionIds\":\\[[^\\]]*\\]", "");
        json = System.Text.RegularExpressions.Regex.Replace(json, ",\"encounteredEnemyIds\":\\[[^\\]]*\\]", "");
        // Phase 8 journeyLog フィールドも v4 にはないので削除
        json = System.Text.RegularExpressions.Regex.Replace(json, ",\"journeyLog\":\\[[^\\]]*\\]", "");
        return json;
    }
}
