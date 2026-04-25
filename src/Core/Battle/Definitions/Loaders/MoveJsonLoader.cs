using System;
using System.Collections.Generic;
using System.Text.Json;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Battle.Definitions.Loaders;

/// <summary>
/// 1 個分の Move JSON を MoveDefinition に変換する純粋関数群。
/// 敵 / 召喚キャラの両方の loader から利用される。
/// Phase 10 設計書（10.1.B）第 4-1 章参照。
/// </summary>
public static class MoveJsonLoader
{
    /// <summary>
    /// 単一 move オブジェクトを MoveDefinition に変換する。
    /// 必須フィールド (id / kind / nextMoveId / effects) が欠落していれば makeException 経由で送出。
    /// </summary>
    public static MoveDefinition ParseMove(JsonElement el, Func<string, Exception> makeException)
    {
        var id = GetRequiredString(el, "id", makeException);
        var kindStr = GetRequiredString(el, "kind", makeException);
        var nextMoveId = GetRequiredString(el, "nextMoveId", makeException);
        var kind = ParseKind(kindStr, makeException);

        if (!el.TryGetProperty("effects", out var effectsEl) || effectsEl.ValueKind != JsonValueKind.Array)
            throw makeException($"必須フィールド \"effects\" (array) がありません (move id={id})。");

        var effects = new List<CardEffect>();
        int idx = 0;
        foreach (var effEl in effectsEl.EnumerateArray())
        {
            effects.Add(CardEffectParser.ParseEffect(
                effEl,
                msg => makeException($"{msg} (move id={id}, effects[{idx}])")));
            idx++;
        }

        return new MoveDefinition(id, kind, effects, nextMoveId);
    }

    private static MoveKind ParseKind(string s, Func<string, Exception> makeException) => s switch
    {
        "Attack"  => MoveKind.Attack,
        "Defend"  => MoveKind.Defend,
        "Buff"    => MoveKind.Buff,
        "Debuff"  => MoveKind.Debuff,
        "Heal"    => MoveKind.Heal,
        "Multi"   => MoveKind.Multi,
        "Unknown" => MoveKind.Unknown,
        _ => throw makeException(
            $"未知の MoveKind 値: \"{s}\"。'Attack'/'Defend'/'Buff'/'Debuff'/'Heal'/'Multi'/'Unknown' のいずれか。"),
    };

    private static string GetRequiredString(JsonElement el, string key, Func<string, Exception> mk)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String)
            throw mk($"必須フィールド \"{key}\" (string) がありません。");
        return v.GetString()!;
    }
}
