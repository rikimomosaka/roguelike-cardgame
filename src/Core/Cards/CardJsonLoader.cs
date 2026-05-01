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

                // Phase 10.5.H: versioned 検出 — versions プロパティが配列なら versioned、それ以外は flat (legacy)。
                if (root.TryGetProperty("versions", out var versionsEl) &&
                    versionsEl.ValueKind == JsonValueKind.Array)
                {
                    var activeSpec = ResolveActiveSpec(root, versionsEl, id);
                    return ParseSpec(id, name, displayName, activeSpec);
                }

                // flat (legacy): root 自体を spec として扱う。
                return ParseSpec(id, name, displayName, root);
            }
            catch (CardJsonException) { throw; }
            catch (Exception ex)
            {
                var where = id is null ? "(card id unknown)" : $"(card id={id})";
                throw new CardJsonException($"カード JSON のパースに失敗しました {where}: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// versioned JSON から activeVersion が指す version の spec 要素を返す。
    /// activeVersion が無い／対応する version が無ければ例外。
    /// </summary>
    private static JsonElement ResolveActiveSpec(JsonElement root, JsonElement versionsEl, string id)
    {
        var activeVersion = GetRequiredString(root, "activeVersion", id);
        foreach (var v in versionsEl.EnumerateArray())
        {
            if (v.ValueKind != JsonValueKind.Object) continue;
            if (!v.TryGetProperty("version", out var verEl) || verEl.ValueKind != JsonValueKind.String) continue;
            if (verEl.GetString() != activeVersion) continue;
            if (!v.TryGetProperty("spec", out var specEl) || specEl.ValueKind != JsonValueKind.Object)
                throw new CardJsonException(
                    $"version '{activeVersion}' の spec が object ではありません (card id={id})。");
            return specEl;
        }
        throw new CardJsonException(
            $"activeVersion '{activeVersion}' が versions[] に見つかりません (card id={id})。");
    }

    /// <summary>
    /// 旧 flat ロジックを spec オブジェクトから読み出す形に切り出した共通実装。
    /// flat 形式では root 自体を、versioned 形式では versions[*].spec を渡す。
    /// </summary>
    private static CardDefinition ParseSpec(string id, string name, string? displayName, JsonElement spec)
    {
        var rawRarity = GetRequiredInt(spec, "rarity", id);
        if (!Enum.IsDefined(typeof(CardRarity), rawRarity))
            throw new CardJsonException($"rarity の値 {rawRarity} は無効です (card id={id})。");
        var rarity = (CardRarity)rawRarity;

        var cardType = ParseCardType(GetRequiredString(spec, "cardType", id), id);

        int? cost = spec.TryGetProperty("cost", out var costEl) && costEl.ValueKind == JsonValueKind.Number
            ? costEl.GetInt32() : (int?)null;

        int? upgradedCost = spec.TryGetProperty("upgradedCost", out var ucEl) && ucEl.ValueKind == JsonValueKind.Number
            ? ucEl.GetInt32() : (int?)null;

        var effects = ParseEffects(spec, "effects", id);

        IReadOnlyList<CardEffect>? upgraded;
        if (spec.TryGetProperty("upgradedEffects", out var upgEl))
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
        if (spec.TryGetProperty("keywords", out var kwEl) && kwEl.ValueKind == JsonValueKind.Array)
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
        if (spec.TryGetProperty("upgradedKeywords", out var ukwEl) && ukwEl.ValueKind == JsonValueKind.Array)
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

        string? description = spec.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String
            ? (string.IsNullOrEmpty(d.GetString()) ? null : d.GetString())
            : null;
        string? upgradedDescription = spec.TryGetProperty("upgradedDescription", out var ud) && ud.ValueKind == JsonValueKind.String
            ? (string.IsNullOrEmpty(ud.GetString()) ? null : ud.GetString())
            : null;

        return new CardDefinition(id, name, displayName, rarity, cardType,
            cost, upgradedCost, effects, upgraded, keywords, upgradedKeywords,
            description, upgradedDescription);
    }

    private static IReadOnlyList<CardEffect> ParseEffects(JsonElement spec, string key, string? id)
    {
        if (!spec.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
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
