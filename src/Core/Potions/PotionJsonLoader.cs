using System;
using System.Collections.Generic;
using System.Text.Json;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Potions;

/// <summary>ポーション JSON のパース失敗を表す例外。</summary>
public sealed class PotionJsonException : Exception
{
    public PotionJsonException(string message) : base(message) { }
    public PotionJsonException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>ポーション JSON 文字列を PotionDefinition に変換する純粋関数群。</summary>
public static class PotionJsonLoader
{
    public static PotionDefinition Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new PotionJsonException("ポーション JSON のパースに失敗しました。", ex); }

        using (doc)
        {
            string? id = null;
            try
            {
                var root = doc.RootElement;
                id = GetRequiredString(root, "id", null);
                var name = GetRequiredString(root, "name", id);

                // rarity: 範囲チェック
                var rawRarity = GetRequiredInt(root, "rarity", id);
                if (!Enum.IsDefined(typeof(CardRarity), rawRarity))
                    throw new PotionJsonException($"rarity の値 {rawRarity} は無効です (potion id={id})。");
                var rarity = (CardRarity)rawRarity;

                // usableInBattle: 必須 boolean
                var usableInBattle = GetRequiredBool(root, "usableInBattle", id);
                // usableOutOfBattle: 必須 boolean
                var usableOutOfBattle = GetRequiredBool(root, "usableOutOfBattle", id);

                var effects = ParseEffects(root, "effects", id);

                return new PotionDefinition(id, name, rarity, usableInBattle, usableOutOfBattle, effects);
            }
            catch (PotionJsonException)
            {
                throw; // already contextual
            }
            catch (Exception ex) when (ex is not PotionJsonException)
            {
                var where = id is null ? "(potion id unknown)" : $"(potion id={id})";
                throw new PotionJsonException($"ポーション JSON のパースに失敗しました {where}: {ex.Message}", ex);
            }
        }
    }

    private static IReadOnlyList<CardEffect> ParseEffects(JsonElement root, string key, string? id)
    {
        if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<CardEffect>();

        var list = new List<CardEffect>();
        foreach (var el in arr.EnumerateArray())
            list.Add(ParseEffect(el, id));
        return list;
    }

    private static CardEffect ParseEffect(JsonElement el, string? id)
    {
        var ctx = id is null ? "" : $" (potion id={id})";
        return CardEffectParser.ParseEffect(el, msg => new PotionJsonException($"{msg}{ctx}"));
    }

    private static string GetRequiredString(JsonElement el, string key, string? id)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String)
        {
            var ctx = id is null ? "" : $" (potion id={id})";
            throw new PotionJsonException($"必須フィールド \"{key}\" (string) がありません。{ctx}");
        }
        return v.GetString()!;
    }

    private static int GetRequiredInt(JsonElement el, string key, string? id)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Number)
        {
            var ctx = id is null ? "" : $" (potion id={id})";
            throw new PotionJsonException($"必須フィールド \"{key}\" (number) がありません。{ctx}");
        }
        return v.GetInt32();
    }

    private static bool GetRequiredBool(JsonElement el, string key, string? id)
    {
        if (!el.TryGetProperty(key, out var v) ||
            (v.ValueKind != JsonValueKind.True && v.ValueKind != JsonValueKind.False))
        {
            var ctx = id is null ? "" : $" (potion id={id})";
            throw new PotionJsonException($"必須フィールド \"{key}\" (boolean) がありません。{ctx}");
        }
        return v.GetBoolean();
    }
}
