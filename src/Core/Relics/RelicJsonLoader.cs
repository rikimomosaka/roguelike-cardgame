using System;
using System.Collections.Generic;
using System.Text.Json;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Relics;

/// <summary>レリック JSON のパース失敗を表す例外。</summary>
public sealed class RelicJsonException : Exception
{
    public RelicJsonException(string message) : base(message) { }
    public RelicJsonException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>レリック JSON 文字列を RelicDefinition に変換する純粋関数群。</summary>
public static class RelicJsonLoader
{
    public static RelicDefinition Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new RelicJsonException("レリック JSON のパースに失敗しました。", ex); }

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
                    throw new RelicJsonException($"rarity の値 {rawRarity} は無効です (relic id={id})。");
                var rarity = (CardRarity)rawRarity;

                // trigger: 文字列 → enum パース
                var trigger = ParseTrigger(GetRequiredString(root, "trigger", id), id);

                var effects = ParseEffects(root, "effects", id);

                // description は任意フィールド (図鑑 / ツールチップ向けのフレーバーテキスト)
                var description = root.TryGetProperty("description", out var descEl) && descEl.ValueKind == JsonValueKind.String
                    ? descEl.GetString() ?? string.Empty
                    : string.Empty;

                return new RelicDefinition(id, name, rarity, trigger, effects, description);
            }
            catch (RelicJsonException)
            {
                throw; // already contextual
            }
            catch (Exception ex) when (ex is not RelicJsonException)
            {
                var where = id is null ? "(relic id unknown)" : $"(relic id={id})";
                throw new RelicJsonException($"レリック JSON のパースに失敗しました {where}: {ex.Message}", ex);
            }
        }
    }

    private static RelicTrigger ParseTrigger(string s, string? id) => s switch
    {
        "OnPickup" => RelicTrigger.OnPickup,
        "Passive" => RelicTrigger.Passive,
        "OnBattleStart" => RelicTrigger.OnBattleStart,
        "OnBattleEnd" => RelicTrigger.OnBattleEnd,
        "OnMapTileResolved" => RelicTrigger.OnMapTileResolved,
        "OnTurnStart" => RelicTrigger.OnTurnStart,
        "OnTurnEnd" => RelicTrigger.OnTurnEnd,
        "OnCardPlay" => RelicTrigger.OnCardPlay,
        "OnEnemyDeath" => RelicTrigger.OnEnemyDeath,
        _ => throw new RelicJsonException($"trigger の値 \"{s}\" は無効です (relic id={id})。"),
    };

    private static IReadOnlyList<CardEffect> ParseEffects(JsonElement root, string key, string? id)
    {
        if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<CardEffect>();

        var ctx = id is null ? "" : $" (relic id={id})";
        var list = new List<CardEffect>();
        foreach (var el in arr.EnumerateArray())
            list.Add(CardEffectParser.ParseEffect(el, msg => new RelicJsonException($"{msg}{ctx}")));
        return list;
    }

    private static string GetRequiredString(JsonElement el, string key, string? id)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String)
        {
            var ctx = id is null ? "" : $" (relic id={id})";
            throw new RelicJsonException($"必須フィールド \"{key}\" (string) がありません。{ctx}");
        }
        return v.GetString()!;
    }

    private static int GetRequiredInt(JsonElement el, string key, string? id)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Number)
        {
            var ctx = id is null ? "" : $" (relic id={id})";
            throw new RelicJsonException($"必須フィールド \"{key}\" (number) がありません。{ctx}");
        }
        return v.GetInt32();
    }
}
