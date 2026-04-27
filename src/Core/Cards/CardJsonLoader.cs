using System;
using System.Collections.Generic;
using System.Text.Json;

namespace RoguelikeCardGame.Core.Cards;

/// <summary>カード JSON のパース失敗を表す例外。</summary>
public sealed class CardJsonException : Exception
{
    public CardJsonException(string message) : base(message) { }
    public CardJsonException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>カード JSON 文字列を CardDefinition に変換する純粋関数群。Phase 10 設計書 第 2-3 章参照。</summary>
public static class CardJsonLoader
{
    public static CardDefinition Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new CardJsonException("カード JSON のパースに失敗しました。", ex); }

        using (doc)
        {
            string? id = null;
            try
            {
                var root = doc.RootElement;
                id = GetRequiredString(root, "id", null);
                var name = GetRequiredString(root, "name", id);
                string? displayName = root.TryGetProperty("displayName", out var dn) && dn.ValueKind == JsonValueKind.String
                    ? dn.GetString() : null;

                var rawRarity = GetRequiredInt(root, "rarity", id);
                if (!Enum.IsDefined(typeof(CardRarity), rawRarity))
                    throw new CardJsonException($"rarity の値 {rawRarity} は無効です (card id={id})。");
                var rarity = (CardRarity)rawRarity;

                var cardType = ParseCardType(GetRequiredString(root, "cardType", id), id);

                int? cost = root.TryGetProperty("cost", out var costEl) && costEl.ValueKind == JsonValueKind.Number
                    ? costEl.GetInt32() : (int?)null;

                int? upgradedCost = root.TryGetProperty("upgradedCost", out var ucEl) && ucEl.ValueKind == JsonValueKind.Number
                    ? ucEl.GetInt32() : (int?)null;

                var effects = ParseEffects(root, "effects", id);

                IReadOnlyList<CardEffect>? upgraded;
                if (root.TryGetProperty("upgradedEffects", out var upgEl))
                {
                    if (upgEl.ValueKind == JsonValueKind.Array)
                        upgraded = ParseEffectsFromElement(upgEl, id);
                    else if (upgEl.ValueKind == JsonValueKind.Null)
                        upgraded = null;
                    else
                        throw new CardJsonException(
                            $"upgradedEffects must be an array or absent/null (card id={id})。");
                }
                else
                {
                    upgraded = null;
                }

                IReadOnlyList<string>? keywords = null;
                if (root.TryGetProperty("keywords", out var kwEl) && kwEl.ValueKind == JsonValueKind.Array)
                {
                    var list = new List<string>();
                    foreach (var k in kwEl.EnumerateArray())
                    {
                        if (k.ValueKind != JsonValueKind.String)
                            throw new CardJsonException($"keywords の要素は string でなければなりません (card id={id})。");
                        list.Add(k.GetString()!);
                    }
                    keywords = list;
                }

                IReadOnlyList<string>? upgradedKeywords = null;
                if (root.TryGetProperty("upgradedKeywords", out var ukwEl) && ukwEl.ValueKind == JsonValueKind.Array)
                {
                    var list = new List<string>();
                    foreach (var k in ukwEl.EnumerateArray())
                    {
                        if (k.ValueKind != JsonValueKind.String)
                            throw new CardJsonException($"upgradedKeywords の要素は string でなければなりません (card id={id})。");
                        list.Add(k.GetString()!);
                    }
                    upgradedKeywords = list;
                }

                return new CardDefinition(id, name, displayName, rarity, cardType,
                    cost, upgradedCost, effects, upgraded, keywords, upgradedKeywords);
            }
            catch (CardJsonException) { throw; }
            catch (Exception ex)
            {
                var where = id is null ? "(card id unknown)" : $"(card id={id})";
                throw new CardJsonException($"カード JSON のパースに失敗しました {where}: {ex.Message}", ex);
            }
        }
    }

    private static IReadOnlyList<CardEffect> ParseEffects(JsonElement root, string key, string? id)
    {
        if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<CardEffect>();
        return ParseEffectsFromElement(arr, id);
    }

    private static IReadOnlyList<CardEffect> ParseEffectsFromElement(JsonElement arr, string? id)
    {
        var list = new List<CardEffect>();
        foreach (var el in arr.EnumerateArray())
            list.Add(ParseEffect(el, id));
        return list;
    }

    private static CardEffect ParseEffect(JsonElement el, string? id)
    {
        var ctx = id is null ? "" : $" (card id={id})";
        return CardEffectParser.ParseEffect(el, msg => new CardJsonException($"{msg}{ctx}"));
    }

    private static CardType ParseCardType(string s, string? id) => s switch
    {
        "Unit" => CardType.Unit,
        "Attack" => CardType.Attack,
        "Skill" => CardType.Skill,
        "Power" => CardType.Power,
        "Status" => CardType.Status,
        "Curse" => CardType.Curse,
        _ => throw new CardJsonException($"未知の cardType: {s} (card id={id})"),
    };

    private static string GetRequiredString(JsonElement el, string key, string? id)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String)
        {
            var ctx = id is null ? "" : $" (card id={id})";
            throw new CardJsonException($"必須フィールド \"{key}\" (string) がありません。{ctx}");
        }
        return v.GetString()!;
    }

    private static int GetRequiredInt(JsonElement el, string key, string? id)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Number)
        {
            var ctx = id is null ? "" : $" (card id={id})";
            throw new CardJsonException($"必須フィールド \"{key}\" (number) がありません。{ctx}");
        }
        return v.GetInt32();
    }
}
