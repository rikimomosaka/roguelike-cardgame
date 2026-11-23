using System;
using System.Collections.Generic;
using System.Text.Json;

namespace RoguelikeCardGame.Core.Battle.Definitions.Loaders;

public sealed class EnemyJsonException : Exception
{
    public EnemyJsonException(string message) : base(message) { }
    public EnemyJsonException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>敵 JSON 文字列を EnemyDefinition に変換する純粋関数群。</summary>
public static class EnemyJsonLoader
{
    public static EnemyDefinition Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new EnemyJsonException("敵 JSON のパースに失敗しました。", ex); }

        using (doc)
        {
            string? id = null;
            try
            {
                var root = doc.RootElement;
                id = GetRequiredString(root, "id", null);
                var name = GetRequiredString(root, "name", id);
                var imageId = GetRequiredString(root, "imageId", id);
                var hp = GetRequiredInt(root, "hp", id);

                var act = GetRequiredInt(root, "act", id);
                if (act < 1 || act > 3)
                    throw new EnemyJsonException($"act の値 {act} は 1〜3 の範囲外です (enemy id={id})。");

                var tier = ParseTier(GetRequiredString(root, "tier", id), id);

                // deck を持つ敵はカード制御（moves と同時指定不可）。
                bool hasDeck = root.TryGetProperty("deck", out _);
                bool hasMoves = root.TryGetProperty("moves", out _)
                    || root.TryGetProperty("initialMoveId", out _);
                if (hasDeck && hasMoves)
                    throw new EnemyJsonException(
                        $"deck と moves/initialMoveId は同時に設定できません (enemy id={id})。");

                string initialMoveId;
                IReadOnlyList<MoveDefinition> moves;
                IReadOnlyList<UnitDeckEntry>? deck = null;
                if (hasDeck)
                {
                    try { deck = UnitJsonLoader.ParseDeck(root, id); }
                    catch (UnitJsonException ex)
                    {
                        throw new EnemyJsonException(
                            ex.Message.Replace("unit id=", "enemy id="), ex);
                    }
                    initialMoveId = string.Empty;
                    moves = Array.Empty<MoveDefinition>();
                }
                else
                {
                    initialMoveId = GetRequiredString(root, "initialMoveId", id);
                    moves = ParseMoves(root, "moves", id);
                    if (moves.Count == 0)
                        throw new EnemyJsonException($"moves が空です (enemy id={id})。");

                    bool found = false;
                    foreach (var m in moves) if (m.Id == initialMoveId) { found = true; break; }
                    if (!found)
                        throw new EnemyJsonException(
                            $"initialMoveId \"{initialMoveId}\" が moves に存在しません (enemy id={id})。");
                }

                var heightTier = ParseOptionalIntInRange(root, "heightTier", 5, 1, 10, id);
                var initialEnergy = ParseOptionalIntInRange(
                    root, "initialEnergy", 0, 0, int.MaxValue, id);
                var energyPerTurn = ParseOptionalIntInRange(
                    root, "energyPerTurn", 1, 0, int.MaxValue, id);
                var drawPerTurn = ParseOptionalIntInRange(
                    root, "drawPerTurn", 3, 0, int.MaxValue, id);
                var handLimit = ParseOptionalIntInRange(
                    root, "handLimit", 5, 1, int.MaxValue, id);
                string? aiProfileId = null;
                if (root.TryGetProperty("aiProfileId", out var aiEl))
                {
                    if (aiEl.ValueKind != JsonValueKind.String
                        || string.IsNullOrWhiteSpace(aiEl.GetString()))
                        throw new EnemyJsonException(
                            $"\"aiProfileId\" は空でない文字列である必要があります。 (enemy id={id})");
                    aiProfileId = aiEl.GetString();
                }
                if (hasDeck && aiProfileId is null)
                    throw new EnemyJsonException(
                        $"deck を持つ敵には aiProfileId が必要です (enemy id={id})。");

                return new EnemyDefinition(id, name, imageId, hp,
                    new EnemyPool(act, tier), initialMoveId, moves, heightTier,
                    Deck: deck,
                    InitialEnergy: initialEnergy,
                    EnergyPerTurn: energyPerTurn,
                    DrawPerTurn: drawPerTurn,
                    HandLimit: handLimit,
                    AiProfileId: aiProfileId,
                    Attack: ParseOptionalIntInRange(root, "attack", 0, 0, int.MaxValue, id),
                    Defense: ParseOptionalIntInRange(root, "defense", 0, 0, int.MaxValue, id),
                    OpeningActions: ParseOpeningActions(root, id));
            }
            catch (EnemyJsonException) { throw; }
            catch (Exception ex)
            {
                var where = id is null ? "(enemy id unknown)" : $"(enemy id={id})";
                throw new EnemyJsonException($"敵 JSON のパースに失敗しました {where}: {ex.Message}", ex);
            }
        }
    }

    /// <summary>先制行動: "openingActions": ["cardId", ...]（記載順に実行）。</summary>
    private static IReadOnlyList<string>? ParseOpeningActions(JsonElement root, string? id)
    {
        if (!root.TryGetProperty("openingActions", out var arr)
            || arr.ValueKind == JsonValueKind.Null)
            return null;
        if (arr.ValueKind != JsonValueKind.Array)
            throw new EnemyJsonException($"openingActions は配列である必要があります (enemy id={id})。");
        var list = new List<string>();
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(el.GetString()))
                throw new EnemyJsonException(
                    $"openingActions の要素は空でないカード ID 文字列である必要があります (enemy id={id})。");
            list.Add(el.GetString()!);
        }
        return list;
    }

    private static EnemyTier ParseTier(string s, string? id) => s switch
    {
        "Weak"   => EnemyTier.Weak,
        "Strong" => EnemyTier.Strong,
        "Elite"  => EnemyTier.Elite,
        "Boss"   => EnemyTier.Boss,
        _ => throw new EnemyJsonException($"tier の値 \"{s}\" は無効です (enemy id={id})。"),
    };

    private static IReadOnlyList<MoveDefinition> ParseMoves(JsonElement root, string key, string? id)
    {
        if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            throw new EnemyJsonException($"moves は配列である必要があります (enemy id={id})。");

        var list = new List<MoveDefinition>();
        int index = 0;
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                throw new EnemyJsonException(
                    $"moves[{index}] はオブジェクトである必要があります (enemy id={id})。");

            var ctx = $" (enemy id={id}, moves[{index}])";
            list.Add(MoveJsonLoader.ParseMove(el, msg => new EnemyJsonException($"{msg}{ctx}")));
            index++;
        }
        return list;
    }

    private static string GetRequiredString(JsonElement el, string key, string? id)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String)
        {
            var ctx = id is null ? "" : $" (enemy id={id})";
            throw new EnemyJsonException($"必須フィールド \"{key}\" (string) がありません。{ctx}");
        }
        return v.GetString()!;
    }

    private static int GetRequiredInt(JsonElement el, string key, string? id)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Number)
        {
            var ctx = id is null ? "" : $" (enemy id={id})";
            throw new EnemyJsonException($"必須フィールド \"{key}\" (number) がありません。{ctx}");
        }
        return v.GetInt32();
    }

    private static int ParseOptionalIntInRange(
        JsonElement el, string key, int defaultValue, int min, int max, string? id)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind == JsonValueKind.Null)
            return defaultValue;
        if (v.ValueKind != JsonValueKind.Number)
        {
            var ctx = id is null ? "" : $" (enemy id={id})";
            throw new EnemyJsonException(
                $"\"{key}\" は数値である必要があります。{ctx}");
        }
        int n = v.GetInt32();
        if (n < min || n > max)
        {
            var ctx = id is null ? "" : $" (enemy id={id})";
            throw new EnemyJsonException(
                $"\"{key}\" の値 {n} は {min}..{max} の範囲外です。{ctx}");
        }
        return n;
    }
}
