using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using RoguelikeCardGame.Core.Json;

namespace RoguelikeCardGame.Core.Bestiary;

/// <summary>BestiaryState JSON のパース失敗を表す例外。</summary>
public sealed class BestiaryStateSerializerException : Exception
{
    public BestiaryStateSerializerException(string message) : base(message) { }
    public BestiaryStateSerializerException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>BestiaryState ⇔ JSON 文字列の変換。ID は昇順ソート済みで出力。</summary>
public static class BestiaryStateSerializer
{
    public static string Serialize(BestiaryState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var dto = new
        {
            schemaVersion = state.SchemaVersion,
            discoveredCardBaseIds = state.DiscoveredCardBaseIds.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            discoveredRelicIds = state.DiscoveredRelicIds.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            discoveredPotionIds = state.DiscoveredPotionIds.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            encounteredEnemyIds = state.EncounteredEnemyIds.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
        };
        return JsonSerializer.Serialize(dto, JsonOptions.Default);
    }

    public static BestiaryState Deserialize(string json)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch (JsonException ex)
        { throw new BestiaryStateSerializerException("Bestiary JSON のパースに失敗しました。", ex); }

        if (node is not JsonObject obj)
            throw new BestiaryStateSerializerException("Bestiary JSON のルートがオブジェクトではありません。");

        int version = obj["schemaVersion"]?.GetValue<int>()
            ?? throw new BestiaryStateSerializerException("schemaVersion が存在しません。");
        if (version != BestiaryState.CurrentSchemaVersion)
            throw new BestiaryStateSerializerException(
                $"未対応の schemaVersion: {version} (対応: {BestiaryState.CurrentSchemaVersion})");

        return new BestiaryState(
            SchemaVersion: version,
            DiscoveredCardBaseIds: ReadSet(obj, "discoveredCardBaseIds"),
            DiscoveredRelicIds: ReadSet(obj, "discoveredRelicIds"),
            DiscoveredPotionIds: ReadSet(obj, "discoveredPotionIds"),
            EncounteredEnemyIds: ReadSet(obj, "encounteredEnemyIds"));
    }

    private static ImmutableHashSet<string> ReadSet(JsonObject obj, string key)
    {
        if (obj[key] is not JsonArray arr) return ImmutableHashSet<string>.Empty;
        var builder = ImmutableHashSet.CreateBuilder<string>();
        foreach (var n in arr)
        {
            var s = n?.GetValue<string>();
            if (!string.IsNullOrEmpty(s)) builder.Add(s);
        }
        return builder.ToImmutable();
    }
}
