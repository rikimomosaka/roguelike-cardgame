using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Json;

namespace RoguelikeCardGame.Core.Run;

public sealed class RunStateSerializerException : Exception
{
    public RunStateSerializerException(string message) : base(message) { }
    public RunStateSerializerException(string message, Exception inner) : base(message, inner) { }
}

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

        if (version == 3) obj = MigrateV3ToV4(obj);
        else if (version != RunState.CurrentSchemaVersion)
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
        if (obj["deck"] is JsonArray deckV3)
        {
            var deckV4 = new JsonArray();
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
            obj["deck"] = deckV4;
        }
        obj["activeMerchant"] ??= null;
        obj["activeEvent"] ??= null;
        obj["activeRestPending"] ??= false;
        obj["schemaVersion"] = RunState.CurrentSchemaVersion;
        return obj;
    }
}
