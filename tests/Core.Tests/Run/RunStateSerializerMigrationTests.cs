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

    private static string BuildMinimalV4Json()
    {
        // 作成: 最小 v4 run を NewSoloRun で作り、schemaVersion を 4 に書き換えて serialize。
        var cat = RoguelikeCardGame.Core.Data.EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat);
        var json = RunStateSerializer.Serialize(s);
        // schemaVersion を 4 に書き換える
        json = json.Replace("\"schemaVersion\":5", "\"schemaVersion\":4");
        // runId / activeActStartRelicChoice フィールドを削除して v4 体裁に戻す
        json = System.Text.RegularExpressions.Regex.Replace(
            json, ",\"runId\":\"[^\"]*\"", "");
        json = System.Text.RegularExpressions.Regex.Replace(
            json, ",\"activeActStartRelicChoice\":(null|\\{[^}]*\\})", "");
        return json;
    }
}
