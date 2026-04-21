using System;
using System.Collections.Generic;
using System.Text.Json;

namespace RoguelikeCardGame.Core.Enemy;

public sealed class EnemyJsonException : Exception
{
    public EnemyJsonException(string message) : base(message) { }
    public EnemyJsonException(string message, Exception inner) : base(message, inner) { }
}

public static class EnemyJsonLoader
{
    public static EnemyDefinition Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new EnemyJsonException("敵 JSON のパースに失敗しました。", ex); }

        using (doc)
        {
            string? id = null;
            try
            {
                var root = doc.RootElement;
                id = GetRequiredString(root, "id", null);
                var name = GetRequiredString(root, "name", id);
                var imageId = GetRequiredString(root, "imageId", id);

                var hpMin = GetRequiredInt(root, "hpMin", id);
                var hpMax = GetRequiredInt(root, "hpMax", id);
                if (hpMin > hpMax)
                    throw new EnemyJsonException($"hpMin ({hpMin}) は hpMax ({hpMax}) 以下である必要があります (enemy id={id})。");

                var act = GetRequiredInt(root, "act", id);
                if (act < 1 || act > 3)
                    throw new EnemyJsonException($"act の値 {act} は 1〜3 の範囲外です (enemy id={id})。");

                var tier = ParseTier(GetRequiredString(root, "tier", id), id);

                var initialMoveId = GetRequiredString(root, "initialMoveId", id);
                var moves = ParseMoves(root, "moves", id);
                if (moves.Count == 0)
                    throw new EnemyJsonException($"moves が空です (enemy id={id})。");

                bool found = false;
                foreach (var m in moves) if (m.Id == initialMoveId) { found = true; break; }
                if (!found)
                    throw new EnemyJsonException(
                        $"initialMoveId \"{initialMoveId}\" が moves に存在しません (enemy id={id})。");

                return new EnemyDefinition(id, name, imageId, hpMin, hpMax,
                    new EnemyPool(act, tier), initialMoveId, moves);
            }
            catch (EnemyJsonException) { throw; }
            catch (Exception ex)
            {
                var where = id is null ? "(enemy id unknown)" : $"(enemy id={id})";
                throw new EnemyJsonException($"敵 JSON のパースに失敗しました {where}: {ex.Message}", ex);
            }
        }
    }

    private static EnemyTier ParseTier(string s, string? id) => s switch
    {
        "Weak" => EnemyTier.Weak,
        "Strong" => EnemyTier.Strong,
        "Elite" => EnemyTier.Elite,
        "Boss" => EnemyTier.Boss,
        _ => throw new EnemyJsonException($"tier の値 \"{s}\" は無効です (enemy id={id})。"),
    };

    private static IReadOnlyList<MoveDefinition> ParseMoves(JsonElement root, string key, string? id)
    {
        if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            throw new EnemyJsonException($"moves は配列である必要があります (enemy id={id})。");

        var list = new List<MoveDefinition>();
        int index = 0;
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                throw new EnemyJsonException(
                    $"moves[{index}] はオブジェクトである必要があります (enemy id={id})。");

            var mid = GetRequiredString(el, "id", id);
            var kind = GetRequiredString(el, "kind", id);
            var nextMoveId = GetRequiredString(el, "nextMoveId", id);

            int? dmin = GetOptionalInt(el, "damageMin");
            int? dmax = GetOptionalInt(el, "damageMax");
            int? hits = GetOptionalInt(el, "hits");
            int? bmin = GetOptionalInt(el, "blockMin");
            int? bmax = GetOptionalInt(el, "blockMax");
            string? buff = GetOptionalString(el, "buff");
            int? amin = GetOptionalInt(el, "amountMin");
            int? amax = GetOptionalInt(el, "amountMax");

            list.Add(new MoveDefinition(mid, kind, dmin, dmax, hits, bmin, bmax, buff, amin, amax, nextMoveId));
            index++;
        }
        return list;
    }

    private static string GetRequiredString(JsonElement el, string key, string? id)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String)
        {
            var ctx = id is null ? "" : $" (enemy id={id})";
            throw new EnemyJsonException($"必須フィールド \"{key}\" (string) がありません。{ctx}");
        }
        return v.GetString()!;
    }

    private static int GetRequiredInt(JsonElement el, string key, string? id)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Number)
        {
            var ctx = id is null ? "" : $" (enemy id={id})";
            throw new EnemyJsonException($"必須フィールド \"{key}\" (number) がありません。{ctx}");
        }
        return v.GetInt32();
    }

    private static int? GetOptionalInt(JsonElement el, string key)
        => el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    private static string? GetOptionalString(JsonElement el, string key)
        => el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
