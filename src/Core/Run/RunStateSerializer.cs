using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using RoguelikeCardGame.Core.Json;

namespace RoguelikeCardGame.Core.Run;

/// <summary>RunState JSON のパース失敗を表す例外。</summary>
public sealed class RunStateSerializerException : Exception
{
    public RunStateSerializerException(string message) : base(message) { }
    public RunStateSerializerException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>RunState ⇔ JSON 文字列の変換。ファイル I/O は Server 側の ISaveRepository が担当。</summary>
public static class RunStateSerializer
{
    public static string Serialize(RunState state)
        => JsonSerializer.Serialize(state, JsonOptions.Default);

    public static RunState Deserialize(string json)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch (JsonException ex)
        { throw new RunStateSerializerException("RunState JSON のパースに失敗しました。", ex); }

        if (node is not JsonObject obj)
            throw new RunStateSerializerException("RunState JSON のルートがオブジェクトではありません。");

        int version = obj["schemaVersion"]?.GetValue<int>()
            ?? throw new RunStateSerializerException("schemaVersion が存在しません。");

        if (version == 3) { obj = MigrateV3ToV4(obj); version = 4; }
        if (version == 4) { obj = MigrateV4ToV5(obj); version = 5; }
        if (version != RunState.CurrentSchemaVersion)
            throw new RunStateSerializerException(
                $"未対応の schemaVersion: {version} (対応: {RunState.CurrentSchemaVersion})");

        RunState? state;
        try { state = JsonSerializer.Deserialize<RunState>(obj.ToJsonString(), JsonOptions.Default); }
        catch (JsonException ex)
        { throw new RunStateSerializerException("RunState JSON のパースに失敗しました。", ex); }

        if (state is null) throw new RunStateSerializerException("RunState JSON が null でした。");
        return state;
    }

    private static JsonObject MigrateV3ToV4(JsonObject obj)
    {
        // Deck: string[] → CardInstance[] with Upgraded=false
        if (obj["deck"] is not JsonArray deckV3)
            throw new RunStateSerializerException("v3 の deck が配列ではありません。");

        var deckV4 = new JsonArray();
        try
        {
            foreach (var idNode in deckV3)
            {
                var id = idNode?.GetValue<string>()
                    ?? throw new RunStateSerializerException("deck に null 要素が含まれています。");
                deckV4.Add(new JsonObject
                {
                    ["id"] = id,
                    ["upgraded"] = false,
                });
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or JsonException)
        {
            throw new RunStateSerializerException("v3 の deck 要素が string ではありません。", ex);
        }
        obj["deck"] = deckV4;

        obj["activeMerchant"] ??= null;
        obj["activeEvent"] ??= null;
        obj["activeRestPending"] ??= false;
        obj["schemaVersion"] = 4;
        return obj;
    }

    private static JsonObject MigrateV4ToV5(JsonObject obj)
    {
        obj["runId"] ??= Guid.NewGuid().ToString();
        obj["activeActStartRelicChoice"] ??= null;
        // v4 セーブは Start が既に visited 済みなので VisitedNodeIds をそのまま引き継ぐ
        // (自然と act-start relic スキップとして扱われる、spec の migration ルール通り)
        // RewardState.isBossReward は JSON 側で default (false) のまま問題なし
        obj["schemaVersion"] = RunState.CurrentSchemaVersion;
        return obj;
    }
}
