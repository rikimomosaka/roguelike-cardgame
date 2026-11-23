using System;
using System.Collections.Generic;
using System.Text.Json;

namespace RoguelikeCardGame.Core.Cards;

/// <summary>
/// カード／レリック／ポーション／敵 Move 共通で使う、新形式 CardEffect の JSON パーサー。
/// Phase 10 設計書 第 2-1, 2-2 章参照。
/// </summary>
public static class CardEffectParser
{
    /// <summary>
    /// 単一 effect オブジェクトを CardEffect に変換し、Normalize() を適用して返す。
    /// 必須フィールド (action / scope / amount) が欠落していれば makeException 経由で送出。
    /// </summary>
    public static CardEffect ParseEffect(JsonElement el, Func<string, Exception> makeException)
    {
        var action = GetRequiredString(el, "action", makeException);
        var scope = ParseScope(GetRequiredString(el, "scope", makeException), makeException);

        var statusChanges = ParseStatusChanges(el, action, makeException);
        bool hasGroupedStatuses = statusChanges is not null;
        if (hasGroupedStatuses &&
            (el.TryGetProperty("name", out _) ||
             el.TryGetProperty("amount", out _) ||
             el.TryGetProperty("amountSource", out _)))
        {
            throw makeException(
                "statusChanges cannot be combined with top-level name, amount, or amountSource.");
        }
        var amount = hasGroupedStatuses ? 0 : GetRequiredInt(el, "amount", makeException);

        EffectSide? side = null;
        if (el.TryGetProperty("side", out var sideEl) && sideEl.ValueKind == JsonValueKind.String)
            side = ParseSide(sideEl.GetString()!, makeException);

        string? name = GetOptionalString(el, "name");
        string? unitId = GetOptionalString(el, "unitId");
        int? comboMin = GetOptionalInt(el, "comboMin");
        int? maxComboMin = null;
        if (el.TryGetProperty("maxComboMin", out var maxComboMinEl))
        {
            if (maxComboMinEl.ValueKind != JsonValueKind.Number ||
                !maxComboMinEl.TryGetInt32(out var parsedMaxComboMin))
            {
                throw makeException("maxComboMin must be an integer when specified.");
            }
            if (parsedMaxComboMin <= 0)
                throw makeException($"maxComboMin must be positive when specified, got {parsedMaxComboMin}.");
            maxComboMin = parsedMaxComboMin;
        }
        string? pile = GetOptionalString(el, "pile");
        bool battleOnly = el.TryGetProperty("battleOnly", out var boEl)
                          && boEl.ValueKind == JsonValueKind.True;

        // 10.5.B reserved fields (engine は本フェーズでは無視。10.5.D-F で順次対応)
        string? cardRefId = GetOptionalString(el, "cardRefId");
        string? select = GetOptionalString(el, "select");
        string? amountSource = GetOptionalString(el, "amountSource");
        string? trigger = GetOptionalString(el, "trigger");
        int? chancePercent = el.TryGetProperty("chancePercent", out var chanceEl)
            ? chanceEl.GetInt32()
            : null;
        if (chancePercent is < 0 or > 100)
            throw makeException($"chancePercent must be between 0 and 100, got {chancePercent}");
        string? costDuration = GetOptionalString(el, "costDuration");
        int? cardCount = GetOptionalInt(el, "cardCount");
        int? amountScalePercent = GetOptionalInt(el, "amountScalePercent");
        if (amountScalePercent is <= 0)
            throw makeException($"amountScalePercent must be positive when specified, got {amountScalePercent}");
        if (amountScalePercent is not null && string.IsNullOrEmpty(amountSource))
            throw makeException("amountScalePercent requires amountSource.");
        if (cardCount is <= 0)
            throw makeException($"cardCount must be positive when specified, got {cardCount}");
        if (el.TryGetProperty("usesPerTurn", out _))
            throw makeException(
                "usesPerTurn は個々のeffectではなく、カード・ポーション・キャラクタースキル本体に設定してください。");

        var raw = new CardEffect(action, scope, side, amount,
            Name: name, UnitId: unitId, ComboMin: comboMin, Pile: pile, BattleOnly: battleOnly,
            CardRefId: cardRefId, Select: select, AmountSource: amountSource, Trigger: trigger,
            ChancePercent: chancePercent, CostDuration: costDuration, CardCount: cardCount,
            StatusChanges: statusChanges, MaxComboMin: maxComboMin,
            AmountScalePercent: amountScalePercent);
        return raw.Normalize();
    }

    private static IReadOnlyList<StatusChange>? ParseStatusChanges(
        JsonElement el,
        string action,
        Func<string, Exception> makeException)
    {
        if (!el.TryGetProperty("statusChanges", out var changesEl))
            return null;
        if (action is not ("buff" or "debuff"))
            throw makeException("statusChanges is supported only by buff/debuff effects.");
        if (changesEl.ValueKind != JsonValueKind.Array || changesEl.GetArrayLength() == 0)
            throw makeException("statusChanges must be a non-empty array.");

        var changes = new List<StatusChange>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var changeEl in changesEl.EnumerateArray())
        {
            if (changeEl.ValueKind != JsonValueKind.Object)
                throw makeException("each statusChanges entry must be an object.");
            string name = GetRequiredString(changeEl, "name", makeException);
            int amount = GetRequiredInt(changeEl, "amount", makeException);
            if (!names.Add(name))
                throw makeException($"statusChanges contains duplicate status '{name}'.");
            changes.Add(new StatusChange(name, amount));
        }
        return changes;
    }

    private static EffectScope ParseScope(string s, Func<string, Exception> makeException) => s switch
    {
        "self" => EffectScope.Self,
        "single" => EffectScope.Single,
        "random" => EffectScope.Random,
        "all" => EffectScope.All,
        _ => throw makeException($"未知の scope 値: \"{s}\"。'self'/'single'/'random'/'all' のいずれか。"),
    };

    private static EffectSide ParseSide(string s, Func<string, Exception> makeException) => s switch
    {
        "enemy" => EffectSide.Enemy,
        "ally" => EffectSide.Ally,
        _ => throw makeException($"未知の side 値: \"{s}\"。'enemy'/'ally' のいずれか。"),
    };

    private static string GetRequiredString(JsonElement el, string key, Func<string, Exception> mk)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String)
            throw mk($"必須フィールド \"{key}\" (string) がありません。");
        return v.GetString()!;
    }

    private static int GetRequiredInt(JsonElement el, string key, Func<string, Exception> mk)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Number)
            throw mk($"必須フィールド \"{key}\" (number) がありません。");
        return v.GetInt32();
    }

    private static string? GetOptionalString(JsonElement el, string key)
        => el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetOptionalInt(JsonElement el, string key)
        => el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
}
