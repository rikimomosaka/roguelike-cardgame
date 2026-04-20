using System;
using System.Text.Json;

namespace RoguelikeCardGame.Core.Cards;

/// <summary>
/// カード／レリック／ポーションなど複数ローダーで共有する、
/// CardEffect（"type" ディスクリミネータ付き）を JsonElement から解析するヘルパー。
/// 各ローダーは自分の例外型を返すファクトリ（<paramref name="makeException"/>）を渡し、
/// id 等のコンテキストをメッセージに含める責務を持つ。
/// </summary>
public static class CardEffectParser
{
    /// <summary>
    /// 単一の effect オブジェクト（<c>{ "type": ..., "amount": ... }</c>）を CardEffect に変換する。
    /// 既知の type は専用型（DamageEffect / GainBlockEffect）、未知の type は UnknownEffect として返す。
    /// 必須フィールド欠落時は <paramref name="makeException"/> が生成した例外を送出する。
    /// </summary>
    public static CardEffect ParseEffect(JsonElement el, Func<string, Exception> makeException)
    {
        if (!el.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
            throw makeException("必須フィールド \"type\" (string) がありません。");
        var type = typeEl.GetString()!;
        return type switch
        {
            "damage" => new DamageEffect(GetRequiredInt(el, "amount", makeException)),
            "gainBlock" => new GainBlockEffect(GetRequiredInt(el, "amount", makeException)),
            _ => new UnknownEffect(type),
        };
    }

    private static int GetRequiredInt(JsonElement el, string key, Func<string, Exception> makeException)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Number)
            throw makeException($"必須フィールド \"{key}\" (number) がありません。");
        return v.GetInt32();
    }
}
