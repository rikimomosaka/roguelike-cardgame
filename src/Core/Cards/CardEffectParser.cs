using System;
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
        var amount = GetRequiredInt(el, "amount", makeException);

        EffectSide? side = null;
        if (el.TryGetProperty("side", out var sideEl) && sideEl.ValueKind == JsonValueKind.String)
            side = ParseSide(sideEl.GetString()!, makeException);

        string? name = GetOptionalString(el, "name");
        string? unitId = GetOptionalString(el, "unitId");
        int? comboMin = GetOptionalInt(el, "comboMin");
        string? pile = GetOptionalString(el, "pile");
        bool battleOnly = el.TryGetProperty("battleOnly", out var boEl)
                          && boEl.ValueKind == JsonValueKind.True;

        // 10.5.B reserved fields (engine は本フェーズでは無視。10.5.D-F で順次対応)
        string? cardRefId = GetOptionalString(el, "cardRefId");
        string? select = GetOptionalString(el, "select");
        string? amountSource = GetOptionalString(el, "amountSource");
        string? trigger = GetOptionalString(el, "trigger");

        var raw = new CardEffect(action, scope, side, amount,
            Name: name, UnitId: unitId, ComboMin: comboMin, Pile: pile, BattleOnly: battleOnly,
            CardRefId: cardRefId, Select: select, AmountSource: amountSource, Trigger: trigger);
        return raw.Normalize();
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
