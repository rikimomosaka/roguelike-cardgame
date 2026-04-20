using System;
using System.Collections.Generic;
using System.Text.Json;

namespace RoguelikeCardGame.Core.Enemy;

/// <summary>敵 JSON のパース失敗を表す例外。</summary>
public sealed class EnemyJsonException : Exception
{
    public EnemyJsonException(string message) : base(message) { }
    public EnemyJsonException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>敵 JSON 文字列を EnemyDefinition に変換する純粋関数群。</summary>
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

                var hpMin = GetRequiredInt(root, "hpMin", id);
                var hpMax = GetRequiredInt(root, "hpMax", id);
                if (hpMin > hpMax)
                    throw new EnemyJsonException($"hpMin ({hpMin}) は hpMax ({hpMax}) 以下である必要があります (enemy id={id})。");

                var act = GetRequiredInt(root, "act", id);
                if (act < 1 || act > 3)
                    throw new EnemyJsonException($"act の値 {act} は 1〜3 の範囲外です (enemy id={id})。");

                // tier: 文字列 → enum パース
                var tierStr = GetRequiredString(root, "tier", id);
                if (!Enum.TryParse<EnemyTier>(tierStr, out var tier))
                    throw new EnemyJsonException($"tier の値 \"{tierStr}\" は無効です (enemy id={id})。");

                var moveset = ParseMoveset(root, "moveset", id);

                return new EnemyDefinition(id, name, hpMin, hpMax, new EnemyPool(act, tier), moveset);
            }
            catch (EnemyJsonException)
            {
                throw; // already contextual
            }
            catch (Exception ex) when (ex is not EnemyJsonException)
            {
                var where = id is null ? "(enemy id unknown)" : $"(enemy id={id})";
                throw new EnemyJsonException($"敵 JSON のパースに失敗しました {where}: {ex.Message}", ex);
            }
        }
    }

    private static IReadOnlyList<string> ParseMoveset(JsonElement root, string key, string? id)
    {
        if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var list = new List<string>();
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.String)
            {
                var ctx = id is null ? "" : $" (enemy id={id})";
                throw new EnemyJsonException($"moveset の要素は文字列である必要があります。{ctx}");
            }
            list.Add(el.GetString()!);
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
}
