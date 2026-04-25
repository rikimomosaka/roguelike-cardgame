using System;
using System.Collections.Generic;
using System.Text.Json;

namespace RoguelikeCardGame.Core.Battle.Definitions.Loaders;

public sealed class UnitJsonException : Exception
{
    public UnitJsonException(string message) : base(message) { }
    public UnitJsonException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>召喚キャラ JSON 文字列を UnitDefinition に変換する純粋関数群。</summary>
public static class UnitJsonLoader
{
    public static UnitDefinition Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new UnitJsonException("召喚キャラ JSON のパースに失敗しました。", ex); }

        using (doc)
        {
            string? id = null;
            try
            {
                var root = doc.RootElement;
                id = GetRequiredString(root, "id", null);
                var name = GetRequiredString(root, "name", id);
                var imageId = GetRequiredString(root, "imageId", id);
                var hp = GetRequiredInt(root, "hp", id);
                var initialMoveId = GetRequiredString(root, "initialMoveId", id);

                var moves = ParseMoves(root, "moves", id);
                if (moves.Count == 0)
                    throw new UnitJsonException($"moves が空です (unit id={id})。");

                bool found = false;
                foreach (var m in moves) if (m.Id == initialMoveId) { found = true; break; }
                if (!found)
                    throw new UnitJsonException(
                        $"initialMoveId \"{initialMoveId}\" が moves に存在しません (unit id={id})。");

                int? lifetime = null;
                if (root.TryGetProperty("lifetimeTurns", out var ltEl) && ltEl.ValueKind == JsonValueKind.Number)
                    lifetime = ltEl.GetInt32();

                return new UnitDefinition(id, name, imageId, hp, initialMoveId, moves, lifetime);
            }
            catch (UnitJsonException) { throw; }
            catch (Exception ex)
            {
                var where = id is null ? "(unit id unknown)" : $"(unit id={id})";
                throw new UnitJsonException($"召喚キャラ JSON のパースに失敗しました {where}: {ex.Message}", ex);
            }
        }
    }

    private static IReadOnlyList<MoveDefinition> ParseMoves(JsonElement root, string key, string? id)
    {
        if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            throw new UnitJsonException($"moves は配列である必要があります (unit id={id})。");

        var list = new List<MoveDefinition>();
        int index = 0;
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                throw new UnitJsonException(
                    $"moves[{index}] はオブジェクトである必要があります (unit id={id})。");

            var ctx = $" (unit id={id}, moves[{index}])";
            list.Add(MoveJsonLoader.ParseMove(el, msg => new UnitJsonException($"{msg}{ctx}")));
            index++;
        }
        return list;
    }

    private static string GetRequiredString(JsonElement el, string key, string? id)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String)
        {
            var ctx = id is null ? "" : $" (unit id={id})";
            throw new UnitJsonException($"必須フィールド \"{key}\" (string) がありません。{ctx}");
        }
        return v.GetString()!;
    }

    private static int GetRequiredInt(JsonElement el, string key, string? id)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Number)
        {
            var ctx = id is null ? "" : $" (unit id={id})";
            throw new UnitJsonException($"必須フィールド \"{key}\" (number) がありません。{ctx}");
        }
        return v.GetInt32();
    }
}
