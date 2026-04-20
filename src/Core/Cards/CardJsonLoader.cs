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

/// <summary>カード JSON 文字列を CardDefinition に変換する純粋関数群。</summary>
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

                // rarity: 範囲チェックを追加
                var rawRarity = GetRequiredInt(root, "rarity", id);
                if (!System.Enum.IsDefined(typeof(CardRarity), rawRarity))
                    throw new CardJsonException($"rarity の値 {rawRarity} は無効です (card id={id})。");
                var rarity = (CardRarity)rawRarity;

                var cardType = ParseCardType(GetRequiredString(root, "cardType", id), id);
                int? cost = root.TryGetProperty("cost", out var costEl) && costEl.ValueKind == JsonValueKind.Number
                    ? costEl.GetInt32() : (int?)null;

                var effects = ParseEffects(root, "effects", id);

                // upgradedEffects: absent/null → null、array → 解析、それ以外 → 例外
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

                return new CardDefinition(id, name, displayName, rarity, cardType, cost, effects, upgraded);
            }
            catch (CardJsonException)
            {
                throw; // already contextual
            }
            catch (Exception ex) when (ex is not CardJsonException)
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
        var type = GetRequiredString(el, "type", id);
        return type switch
        {
            "damage" => new DamageEffect(GetRequiredInt(el, "amount", id)),
            "gainBlock" => new GainBlockEffect(GetRequiredInt(el, "amount", id)),
            _ => new UnknownEffect(type),
        };
    }

    private static CardType ParseCardType(string s, string? id) => s switch
    {
        "Unit" => CardType.Unit,
        "Attack" => CardType.Attack,
        "Skill" => CardType.Skill,
        "Power" => CardType.Power,
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
