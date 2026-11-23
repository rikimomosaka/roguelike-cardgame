using System;
using System.Collections.Generic;
using System.Text.Json;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;

namespace RoguelikeCardGame.Core.Battle.Definitions.Loaders;

public sealed class UnitJsonException : Exception
{
    public UnitJsonException(string message) : base(message) { }
    public UnitJsonException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>召喚キャラ JSON 文字列を UnitDefinition に変換する純粋関数群。</summary>
public static class UnitJsonLoader
{
    public static UnitDefinition Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new UnitJsonException("召喚キャラ JSON のパースに失敗しました。", ex); }

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
                bool hasDeck = root.TryGetProperty("deck", out _);
                bool hasMoves = root.TryGetProperty("moves", out _)
                    || root.TryGetProperty("initialMoveId", out _);
                if (hasDeck && hasMoves)
                    throw new UnitJsonException(
                        $"deck と moves/initialMoveId は同時に設定できません (unit id={id})。");

                string initialMoveId;
                IReadOnlyList<MoveDefinition> moves;
                IReadOnlyList<UnitDeckEntry>? deck = null;
                if (hasDeck)
                {
                    deck = ParseDeck(root, id);
                    initialMoveId = string.Empty;
                    moves = System.Array.Empty<MoveDefinition>();
                }
                else
                {
                    initialMoveId = GetRequiredString(root, "initialMoveId", id);
                    moves = ParseMoves(root, "moves", id);
                    if (moves.Count == 0)
                        throw new UnitJsonException($"moves が空です (unit id={id})。");

                    bool found = false;
                    foreach (var m in moves) if (m.Id == initialMoveId) { found = true; break; }
                    if (!found)
                        throw new UnitJsonException(
                            $"initialMoveId \"{initialMoveId}\" が moves に存在しません (unit id={id})。");
                }

                int? lifetime = null;
                if (root.TryGetProperty("lifetimeTurns", out var ltEl) && ltEl.ValueKind == JsonValueKind.Number)
                    lifetime = ltEl.GetInt32();

                var heightTier = ParseOptionalIntInRange(root, "heightTier", 5, 1, 10, id);
                var initialEnergy = ParseOptionalIntInRange(
                    root, "initialEnergy", 1, 0, int.MaxValue, id);
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
                        throw new UnitJsonException(
                            $"\"aiProfileId\" は空でない文字列である必要があります。 (unit id={id})");
                    aiProfileId = aiEl.GetString();
                }
                if (hasDeck && aiProfileId is null)
                    throw new UnitJsonException(
                        $"deck を持つ召喚キャラには aiProfileId が必要です (unit id={id})。");

                string? traitDescription = GetOptionalString(root, "traitDescription", id);
                var skills = ParseSkills(root, id);
                var ownedRelicIds = ParseOptionalStringArray(root, "ownedRelicIds", id);
                var potions = ParseOptionalStringArray(root, "potions", id);
                var attack = ParseOptionalIntInRange(root, "attack", 0, 0, int.MaxValue, id);
                var defense = ParseOptionalIntInRange(root, "defense", 0, 0, int.MaxValue, id);

                return new UnitDefinition(
                    id, name, imageId, hp, initialMoveId, moves, lifetime,
                    HeightTier: heightTier,
                    Deck: deck,
                    InitialEnergy: initialEnergy,
                    EnergyPerTurn: energyPerTurn,
                    DrawPerTurn: drawPerTurn,
                    HandLimit: handLimit,
                    AiProfileId: aiProfileId,
                    TraitDescription: traitDescription,
                    Skills: skills,
                    OwnedRelicIds: ownedRelicIds,
                    Potions: potions,
                    Attack: attack,
                    Defense: defense);
            }
            catch (UnitJsonException) { throw; }
            catch (Exception ex)
            {
                var where = id is null ? "(unit id unknown)" : $"(unit id={id})";
                throw new UnitJsonException($"召喚キャラ JSON のパースに失敗しました {where}: {ex.Message}", ex);
            }
        }
    }

    private static IReadOnlyList<CharacterSkillDefinition>? ParseSkills(
        JsonElement root,
        string unitId)
    {
        if (!root.TryGetProperty("skills", out var skillsEl)
            || skillsEl.ValueKind == JsonValueKind.Null)
            return null;
        if (skillsEl.ValueKind != JsonValueKind.Array)
            throw new UnitJsonException(
                $"skills は配列である必要があります (unit id={unitId})。");

        var result = new List<CharacterSkillDefinition>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        int index = 0;
        foreach (var skillEl in skillsEl.EnumerateArray())
        {
            if (skillEl.ValueKind != JsonValueKind.Object)
                throw new UnitJsonException(
                    $"skills[{index}] はオブジェクトである必要があります (unit id={unitId})。");

            string skillId = GetRequiredString(skillEl, "id", unitId);
            string skillName = GetRequiredString(skillEl, "name", unitId);
            if (!ids.Add(skillId))
                throw new UnitJsonException(
                    $"skill id '{skillId}' が重複しています (unit id={unitId})。");

            int energy = ParseOptionalIntInRange(
                skillEl, "energyCost", 0, 0, int.MaxValue, unitId);
            int comboGauge = ParseOptionalIntInRange(
                skillEl, "comboGaugeCost", 0, 0, int.MaxValue, unitId);
            bool oncePerTurn = ParseOptionalBoolean(
                skillEl, "oncePerTurn", defaultValue: false, unitId);
            int usesPerTurn = ParseOptionalIntInRange(
                skillEl, "usesPerTurn", oncePerTurn ? 1 : 0,
                0, int.MaxValue, unitId);
            bool requiresCardsInHand = ParseOptionalBoolean(
                skillEl, "requiresCardsInHand", defaultValue: false, unitId);
            string? description = GetOptionalString(
                skillEl, "description", unitId);

            if (!skillEl.TryGetProperty("effects", out var effectsEl)
                || effectsEl.ValueKind != JsonValueKind.Array)
                throw new UnitJsonException(
                    $"skill '{skillId}' effects は配列である必要があります (unit id={unitId})。");
            var effects = new List<CardEffect>();
            foreach (var effectEl in effectsEl.EnumerateArray())
            {
                effects.Add(CardEffectParser.ParseEffect(
                    effectEl,
                    message => new UnitJsonException(
                        $"{message} (unit id={unitId}, skill id={skillId})")));
            }
            if (effects.Count == 0)
                throw new UnitJsonException(
                    $"skill '{skillId}' effects を空にはできません (unit id={unitId})。");

            IReadOnlyList<string>? keywords = ParseOptionalStringArray(
                skillEl, "keywords", unitId,
                context: $"skill '{skillId}'");
            result.Add(new CharacterSkillDefinition(
                skillId,
                skillName,
                new ResourceCost(energy, comboGauge),
                oncePerTurn,
                effects,
                description,
                requiresCardsInHand,
                keywords,
                usesPerTurn));
            index++;
        }

        return result;
    }

    private static IReadOnlyList<string>? ParseOptionalStringArray(
        JsonElement root,
        string key,
        string? unitId,
        string? context = null)
    {
        if (!root.TryGetProperty(key, out var arrayEl)
            || arrayEl.ValueKind == JsonValueKind.Null)
            return null;
        if (arrayEl.ValueKind != JsonValueKind.Array)
            throw new UnitJsonException(
                $"{FormatFieldContext(key, context)} は配列である必要があります (unit id={unitId})。");

        var result = new List<string>();
        int index = 0;
        foreach (var valueEl in arrayEl.EnumerateArray())
        {
            if (valueEl.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(valueEl.GetString()))
                throw new UnitJsonException(
                    $"{FormatFieldContext(key, context)}[{index}] は空でない文字列である必要があります (unit id={unitId})。");
            result.Add(valueEl.GetString()!);
            index++;
        }
        return result;
    }

    private static bool ParseOptionalBoolean(
        JsonElement element,
        string key,
        bool defaultValue,
        string? unitId)
    {
        if (!element.TryGetProperty(key, out var value)
            || value.ValueKind == JsonValueKind.Null)
            return defaultValue;
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new UnitJsonException(
                $"'{key}' は真偽値である必要があります (unit id={unitId})。");
        return value.GetBoolean();
    }

    private static string? GetOptionalString(
        JsonElement element,
        string key,
        string? unitId)
    {
        if (!element.TryGetProperty(key, out var value)
            || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new UnitJsonException(
                $"'{key}' は文字列である必要があります (unit id={unitId})。");
        return value.GetString();
    }

    private static string FormatFieldContext(string key, string? context) =>
        context is null ? $"'{key}'" : $"{context} の '{key}'";

    /// <summary>敵定義（EnemyJsonLoader）からも共通利用する。</summary>
    internal static IReadOnlyList<UnitDeckEntry> ParseDeck(JsonElement root, string? id)
    {
        if (!root.TryGetProperty("deck", out var arr) || arr.ValueKind != JsonValueKind.Array)
            throw new UnitJsonException($"deck は配列である必要があります (unit id={id})。");

        var list = new List<UnitDeckEntry>();
        int index = 0;
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                throw new UnitJsonException(
                    $"deck[{index}] はオブジェクトである必要があります (unit id={id})。");

            var cardId = GetRequiredString(el, "cardId", id);
            int count = ParseOptionalIntInRange(
                el, "count", 1, 1, int.MaxValue, id);
            bool upgraded = false;
            if (el.TryGetProperty("upgraded", out var upgradedEl))
            {
                if (upgradedEl.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new UnitJsonException(
                        $"deck[{index}].upgraded は真偽値である必要があります (unit id={id})。");
                upgraded = upgradedEl.GetBoolean();
            }
            int priority = ParseOptionalIntInRange(
                el, "priority", 0, int.MinValue, int.MaxValue, id);
            list.Add(new UnitDeckEntry(cardId, count, upgraded, priority));
            index++;
        }

        if (list.Count == 0)
            throw new UnitJsonException($"deck が空です (unit id={id})。");
        return list;
    }

    private static IReadOnlyList<MoveDefinition> ParseMoves(JsonElement root, string key, string? id)
    {
        if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            throw new UnitJsonException($"moves は配列である必要があります (unit id={id})。");

        var list = new List<MoveDefinition>();
        int index = 0;
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                throw new UnitJsonException(
                    $"moves[{index}] はオブジェクトである必要があります (unit id={id})。");

            var ctx = $" (unit id={id}, moves[{index}])";
            list.Add(MoveJsonLoader.ParseMove(el, msg => new UnitJsonException($"{msg}{ctx}")));
            index++;
        }
        return list;
    }

    private static string GetRequiredString(JsonElement el, string key, string? id)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String)
        {
            var ctx = id is null ? "" : $" (unit id={id})";
            throw new UnitJsonException($"必須フィールド \"{key}\" (string) がありません。{ctx}");
        }
        return v.GetString()!;
    }

    private static int GetRequiredInt(JsonElement el, string key, string? id)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Number)
        {
            var ctx = id is null ? "" : $" (unit id={id})";
            throw new UnitJsonException($"必須フィールド \"{key}\" (number) がありません。{ctx}");
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
            var ctx = id is null ? "" : $" (unit id={id})";
            throw new UnitJsonException(
                $"\"{key}\" は数値である必要があります。{ctx}");
        }
        int n = v.GetInt32();
        if (n < min || n > max)
        {
            var ctx = id is null ? "" : $" (unit id={id})";
            throw new UnitJsonException(
                $"\"{key}\" の値 {n} は {min}..{max} の範囲外です。{ctx}");
        }
        return n;
    }
}
