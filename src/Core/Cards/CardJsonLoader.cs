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
            var root = doc.RootElement;
            var id = GetRequiredString(root, "id");
            var name = GetRequiredString(root, "name");
            string? displayName = root.TryGetProperty("displayName", out var dn) && dn.ValueKind == JsonValueKind.String
                ? dn.GetString() : null;
            var rarity = (CardRarity)GetRequiredInt(root, "rarity");
            var cardType = ParseCardType(GetRequiredString(root, "cardType"));
            int? cost = root.TryGetProperty("cost", out var costEl) && costEl.ValueKind == JsonValueKind.Number
                ? costEl.GetInt32() : (int?)null;

            var effects = ParseEffects(root, "effects");
            IReadOnlyList<CardEffect>? upgraded = root.TryGetProperty("upgradedEffects", out _)
                ? ParseEffects(root, "upgradedEffects")
                : null;

            return new CardDefinition(id, name, displayName, rarity, cardType, cost, effects, upgraded);
        }
    }

    private static IReadOnlyList<CardEffect> ParseEffects(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<CardEffect>();

        var list = new List<CardEffect>();
        foreach (var el in arr.EnumerateArray())
            list.Add(ParseEffect(el));
        return list;
    }

    private static CardEffect ParseEffect(JsonElement el)
    {
        var type = GetRequiredString(el, "type");
        return type switch
        {
            "damage" => new DamageEffect(GetRequiredInt(el, "amount")),
            "gainBlock" => new GainBlockEffect(GetRequiredInt(el, "amount")),
            _ => new UnknownEffect(type),
        };
    }

    private static CardType ParseCardType(string s) => s switch
    {
        "Unit" => CardType.Unit,
        "Attack" => CardType.Attack,
        "Skill" => CardType.Skill,
        "Power" => CardType.Power,
        _ => throw new CardJsonException($"未知の cardType: {s}"),
    };

    private static string GetRequiredString(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String)
            throw new CardJsonException($"必須フィールド \"{key}\" (string) がありません。");
        return v.GetString()!;
    }

    private static int GetRequiredInt(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Number)
            throw new CardJsonException($"必須フィールド \"{key}\" (number) がありません。");
        return v.GetInt32();
    }
}
