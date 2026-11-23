using System;
using System.Collections.Generic;
using System.Text.Json;
using RoguelikeCardGame.Core.Data;

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
                var poolMetadata = ParsePoolMetadata(root, id);

                // Phase 10.5.H: versioned 検出 — versions プロパティが配列なら versioned、それ以外は flat (legacy)。
                if (root.TryGetProperty("versions", out var versionsEl) &&
                    versionsEl.ValueKind == JsonValueKind.Array)
                {
                    var activeSpec = ResolveActiveSpec(root, versionsEl, id);
                    return ParseSpec(id, name, displayName, activeSpec, poolMetadata);
                }

                // flat (legacy): root 自体を spec として扱う。
                return ParseSpec(id, name, displayName, root, poolMetadata);
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
    private static CardDefinition ParseSpec(
        string id,
        string name,
        string? displayName,
        JsonElement spec,
        CardPoolMetadata poolMetadata)
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

        int comboGaugeCost = spec.TryGetProperty("comboGaugeCost", out var cgcEl) &&
                             cgcEl.ValueKind == JsonValueKind.Number
            ? cgcEl.GetInt32()
            : 0;
        if (comboGaugeCost < 0)
            throw new CardJsonException($"comboGaugeCost must be >= 0 (card id={id})");

        int usesPerTurn = 0;
        if (spec.TryGetProperty("usesPerTurn", out var usesEl))
        {
            if (usesEl.ValueKind != JsonValueKind.Number || !usesEl.TryGetInt32(out usesPerTurn))
                throw new CardJsonException($"usesPerTurn must be an integer (card id={id})");
        }
        int? upgradedUsesPerTurn = null;
        if (spec.TryGetProperty("upgradedUsesPerTurn", out var upgradedUsesEl))
        {
            if (upgradedUsesEl.ValueKind != JsonValueKind.Number ||
                !upgradedUsesEl.TryGetInt32(out var upgradedLimit))
                throw new CardJsonException($"upgradedUsesPerTurn must be an integer (card id={id})");
            upgradedUsesPerTurn = upgradedLimit;
        }
        if (usesPerTurn < 0 || upgradedUsesPerTurn < 0)
            throw new CardJsonException($"usesPerTurn values must be >= 0 (card id={id})");

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
        if (spec.TryGetProperty("keywords", out var kwEl))
        {
            if (kwEl.ValueKind is not (JsonValueKind.Array or JsonValueKind.Null))
                throw new CardJsonException($"keywords must be an array or null (card id={id})。");
            if (kwEl.ValueKind == JsonValueKind.Array)
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
        }

        IReadOnlyList<string>? upgradedKeywords = null;
        if (spec.TryGetProperty("upgradedKeywords", out var ukwEl))
        {
            if (ukwEl.ValueKind is not (JsonValueKind.Array or JsonValueKind.Null))
                throw new CardJsonException($"upgradedKeywords must be an array or null (card id={id})。");
            if (ukwEl.ValueKind == JsonValueKind.Array)
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
        }

        string? description = spec.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String
            ? (string.IsNullOrEmpty(d.GetString()) ? null : d.GetString())
            : null;
        string? upgradedDescription = spec.TryGetProperty("upgradedDescription", out var ud) && ud.ValueKind == JsonValueKind.String
            ? (string.IsNullOrEmpty(ud.GetString()) ? null : ud.GetString())
            : null;

        var abilities = ParseAbilities(spec, "abilities", id);
        var upgradedAbilities = ParseAbilities(spec, "upgradedAbilities", id);

        return new CardDefinition(id, name, displayName, rarity, cardType,
            cost, upgradedCost, effects, upgraded, keywords, upgradedKeywords,
            description, upgradedDescription, comboGaugeCost, usesPerTurn, upgradedUsesPerTurn,
            poolMetadata.PoolClass, poolMetadata.PoolKinds, poolMetadata.EligibleCharacterIds,
            abilities, upgradedAbilities);
    }

    private static IReadOnlyList<CardAbilityDefinition>? ParseAbilities(
        JsonElement spec,
        string propertyName,
        string id)
    {
        if (!spec.TryGetProperty(propertyName, out var abilitiesEl) ||
            abilitiesEl.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (abilitiesEl.ValueKind != JsonValueKind.Array)
            throw new CardJsonException($"{propertyName} must be an array or null (card id={id}).");

        var result = new List<CardAbilityDefinition>();
        var kinds = new HashSet<CardAbilityKind>();
        foreach (var abilityEl in abilitiesEl.EnumerateArray())
        {
            if (abilityEl.ValueKind != JsonValueKind.Object)
                throw new CardJsonException($"{propertyName} entries must be objects (card id={id}).");

            var type = GetRequiredString(abilityEl, "type", id);
            CardAbilityDefinition ability = type switch
            {
                "finisher" => new FinisherAbilityDefinition(),
                "reprise" => new RepriseAbilityDefinition(ParseRepriseFilter(abilityEl, id)),
                "contingency" => new ContingencyAbilityDefinition(ParseContingencyTrigger(abilityEl, id)),
                _ => throw new CardJsonException(
                    $"unknown card ability type '{type}' in {propertyName} (card id={id})."),
            };

            if (!kinds.Add(ability.Kind))
                throw new CardJsonException(
                    $"{propertyName} contains duplicate ability '{type}' (card id={id}).");
            result.Add(ability);
        }
        return result;
    }

    private static RepriseCardFilter ParseRepriseFilter(JsonElement abilityEl, string id)
    {
        if (!abilityEl.TryGetProperty("filter", out var filterEl) ||
            filterEl.ValueKind != JsonValueKind.Object)
        {
            throw new CardJsonException(
                $"reprise ability requires an object filter (card id={id}).");
        }

        IReadOnlyList<CardType>? cardTypes = null;
        if (filterEl.TryGetProperty("cardTypes", out var cardTypesEl) &&
            cardTypesEl.ValueKind != JsonValueKind.Null)
        {
            if (cardTypesEl.ValueKind != JsonValueKind.Array)
                throw new CardJsonException($"reprise filter cardTypes must be an array (card id={id}).");
            var values = new List<CardType>();
            var seen = new HashSet<CardType>();
            foreach (var typeEl in cardTypesEl.EnumerateArray())
            {
                if (typeEl.ValueKind != JsonValueKind.String)
                    throw new CardJsonException(
                        $"reprise filter cardTypes must contain strings (card id={id}).");
                var type = ParseCardType(typeEl.GetString()!, id);
                if (!seen.Add(type))
                    throw new CardJsonException(
                        $"reprise filter cardTypes contains duplicate '{type}' (card id={id}).");
                values.Add(type);
            }
            cardTypes = values;
        }

        var requiredKeywords = ParseOptionalDistinctStrings(
            filterEl, "requiredKeywords", "reprise filter", id);
        int? minOriginalCost = GetOptionalStrictInt(filterEl, "minOriginalCost", "reprise filter", id);
        int? maxOriginalCost = GetOptionalStrictInt(filterEl, "maxOriginalCost", "reprise filter", id);
        int? minPaidCost = GetOptionalStrictInt(filterEl, "minPaidCost", "reprise filter", id);
        int? maxPaidCost = GetOptionalStrictInt(filterEl, "maxPaidCost", "reprise filter", id);

        ValidateCostRange(minOriginalCost, maxOriginalCost, "original cost", id);
        ValidateCostRange(minPaidCost, maxPaidCost, "paid cost", id);

        return new RepriseCardFilter(
            cardTypes, minOriginalCost, maxOriginalCost, minPaidCost, maxPaidCost,
            requiredKeywords);
    }

    private static ContingencyTriggerDefinition ParseContingencyTrigger(
        JsonElement abilityEl,
        string id)
    {
        if (!abilityEl.TryGetProperty("trigger", out var triggerEl) ||
            triggerEl.ValueKind != JsonValueKind.Object)
        {
            throw new CardJsonException(
                $"contingency ability requires an object trigger (card id={id}).");
        }

        var timingText = GetRequiredString(triggerEl, "timing", id);
        var timing = timingText switch
        {
            "TurnStart" => ContingencyTriggerTiming.TurnStart,
            "BeforeEnemyAttack" => ContingencyTriggerTiming.BeforeEnemyAttack,
            "ComboReached" => ContingencyTriggerTiming.ComboReached,
            _ => throw new CardJsonException(
                $"unknown contingency trigger timing '{timingText}' (card id={id})."),
        };
        int? threshold = GetOptionalStrictInt(
            triggerEl, "comboThreshold", "contingency trigger", id);

        if (timing == ContingencyTriggerTiming.ComboReached)
        {
            if (threshold is null or <= 0)
                throw new CardJsonException(
                    $"ComboReached contingency requires a positive comboThreshold (card id={id}).");
        }
        else if (threshold is not null)
        {
            throw new CardJsonException(
                $"comboThreshold is valid only for ComboReached contingency (card id={id}).");
        }

        return new ContingencyTriggerDefinition(timing, threshold);
    }

    private static IReadOnlyList<string>? ParseOptionalDistinctStrings(
        JsonElement owner,
        string propertyName,
        string context,
        string id)
    {
        if (!owner.TryGetProperty(propertyName, out var valuesEl) ||
            valuesEl.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (valuesEl.ValueKind != JsonValueKind.Array)
            throw new CardJsonException(
                $"{context} {propertyName} must be an array (card id={id}).");

        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var valueEl in valuesEl.EnumerateArray())
        {
            if (valueEl.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(valueEl.GetString()))
            {
                throw new CardJsonException(
                    $"{context} {propertyName} must contain non-empty strings (card id={id}).");
            }
            var value = valueEl.GetString()!;
            if (!seen.Add(value))
                throw new CardJsonException(
                    $"{context} {propertyName} contains duplicate '{value}' (card id={id}).");
            values.Add(value);
        }
        return values;
    }

    private static int? GetOptionalStrictInt(
        JsonElement owner,
        string propertyName,
        string context,
        string id)
    {
        if (!owner.TryGetProperty(propertyName, out var valueEl) ||
            valueEl.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        if (valueEl.ValueKind != JsonValueKind.Number || !valueEl.TryGetInt32(out var value))
            throw new CardJsonException(
                $"{context} {propertyName} must be an integer (card id={id}).");
        return value;
    }

    private static void ValidateCostRange(int? min, int? max, string label, string id)
    {
        if (min < 0 || max < 0)
            throw new CardJsonException($"reprise {label} limits must be >= 0 (card id={id}).");
        if (min is int lower && max is int upper && lower > upper)
            throw new CardJsonException(
                $"reprise {label} minimum cannot exceed maximum (card id={id}).");
    }

    private static CardPoolMetadata ParsePoolMetadata(JsonElement root, string id)
    {
        var poolKinds = new List<CardPoolKind>();
        if (root.TryGetProperty("poolKinds", out var kindsElement) &&
            kindsElement.ValueKind != JsonValueKind.Null)
        {
            if (kindsElement.ValueKind != JsonValueKind.Array)
                throw new CardJsonException($"poolKinds must be an array (card id={id})");

            var seen = new HashSet<CardPoolKind>();
            foreach (var kindElement in kindsElement.EnumerateArray())
            {
                var kind = ParseEnum<CardPoolKind>(kindElement, "poolKinds", id);
                if (!seen.Add(kind))
                    throw new CardJsonException($"poolKinds contains duplicate value '{kind}' (card id={id})");
                poolKinds.Add(kind);
            }
        }

        CardPoolClass? poolClass = null;
        if (root.TryGetProperty("poolClass", out var classElement) &&
            classElement.ValueKind != JsonValueKind.Null)
        {
            poolClass = ParseEnum<CardPoolClass>(classElement, "poolClass", id);
        }

        var eligibleCharacterIds = new List<string>();
        if (root.TryGetProperty("eligibleCharacterIds", out var eligibleElement) &&
            eligibleElement.ValueKind != JsonValueKind.Null)
        {
            if (eligibleElement.ValueKind != JsonValueKind.Array)
                throw new CardJsonException($"eligibleCharacterIds must be an array (card id={id})");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var characterElement in eligibleElement.EnumerateArray())
            {
                if (characterElement.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(characterElement.GetString()))
                {
                    throw new CardJsonException(
                        $"eligibleCharacterIds must contain non-empty strings (card id={id})");
                }

                var characterId = characterElement.GetString()!;
                if (!seen.Add(characterId))
                {
                    throw new CardJsonException(
                        $"eligibleCharacterIds contains duplicate value '{characterId}' (card id={id})");
                }
                eligibleCharacterIds.Add(characterId);
            }
        }

        return new CardPoolMetadata(poolClass, poolKinds, eligibleCharacterIds);
    }

    private static TEnum ParseEnum<TEnum>(JsonElement element, string propertyName, string id)
        where TEnum : struct, Enum
    {
        if (element.ValueKind != JsonValueKind.String ||
            !Enum.TryParse<TEnum>(element.GetString(), ignoreCase: false, out var value) ||
            !Enum.IsDefined(value))
        {
            var raw = element.ValueKind == JsonValueKind.String
                ? element.GetString()
                : element.ValueKind.ToString();
            throw new CardJsonException(
                $"{propertyName} contains unknown value '{raw}' (card id={id})");
        }
        return value;
    }

    private sealed record CardPoolMetadata(
        CardPoolClass? PoolClass,
        IReadOnlyList<CardPoolKind> PoolKinds,
        IReadOnlyList<string> EligibleCharacterIds);

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
