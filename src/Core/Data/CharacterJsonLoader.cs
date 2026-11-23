using System;
using System.Collections.Generic;
using System.Text.Json;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Data;

public sealed class CharacterJsonException : Exception
{
    public CharacterJsonException(string message) : base(message) { }
    public CharacterJsonException(string message, Exception inner) : base(message, inner) { }
}

public static class CharacterJsonLoader
{
    public static CharacterDefinition Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new CharacterJsonException("character JSON のパース失敗", ex); }

        using (doc)
        {
            var r = doc.RootElement;
            string id = r.GetProperty("id").GetString()!;
            string name = r.GetProperty("name").GetString()!;
            int maxHp = r.GetProperty("maxHp").GetInt32();
            int gold = r.GetProperty("startingGold").GetInt32();
            int slots = r.GetProperty("potionSlotCount").GetInt32();
            var deck = new List<string>();
            foreach (var e in r.GetProperty("deck").EnumerateArray())
                deck.Add(e.GetString()!);

            if (maxHp <= 0) throw new CharacterJsonException($"maxHp must be > 0 (id={id})");
            if (slots < 0) throw new CharacterJsonException($"potionSlotCount must be >= 0 (id={id})");
            if (deck.Count == 0) throw new CharacterJsonException($"deck must not be empty (id={id})");

            var heightTier = ParseOptionalIntInRange(r, "heightTier", 5, 1, 10, id);
            var skills = ParseSkills(r, id);
            string? description = GetOptionalString(r, "description");
            string? traitDescription = GetOptionalString(r, "traitDescription");
            var characterClass = ParseCharacterClass(r, id);
            int energyPerTurn = ParseOptionalIntInRange(
                r, "energyPerTurn", 3, 0, int.MaxValue, id);
            int drawPerTurn = ParseOptionalIntInRange(
                r, "drawPerTurn", 5, 0, int.MaxValue, id);
            var trait = ParseTrait(r, id);
            // 基礎攻撃力・守備力（筋力・敏捷とは別の能力値）。
            int attack = ParseOptionalIntInRange(r, "attack", 0, 0, int.MaxValue, id);
            int defense = ParseOptionalIntInRange(r, "defense", 0, 0, int.MaxValue, id);

            return new CharacterDefinition(id, name, maxHp, gold, slots, deck,
                HeightTier: heightTier, Skills: skills,
                Description: description, TraitDescription: traitDescription,
                CharacterClass: characterClass,
                EnergyPerTurn: energyPerTurn,
                DrawPerTurn: drawPerTurn,
                Trait: trait,
                Attack: attack,
                Defense: defense);
        }
    }

    private static CharacterTraitDefinition? ParseTrait(
        JsonElement root,
        string characterId)
    {
        if (!root.TryGetProperty("trait", out var traitEl) ||
            traitEl.ValueKind == JsonValueKind.Null)
            return null;
        if (traitEl.ValueKind != JsonValueKind.Object)
            throw new CharacterJsonException(
                $"trait must be an object (character id={characterId})");

        string id = GetRequiredNonEmptyString(
            traitEl, "id", characterId, "trait");
        string name = GetRequiredNonEmptyString(
            traitEl, "name", characterId, $"trait id={id}");
        string rawTrigger = GetRequiredNonEmptyString(
            traitEl, "trigger", characterId, $"trait id={id}");
        if (!Enum.TryParse<CharacterTraitTrigger>(
                rawTrigger, ignoreCase: false, out var trigger) ||
            !Enum.IsDefined(trigger))
        {
            throw new CharacterJsonException(
                $"trait '{id}' contains an unknown trigger '{rawTrigger}' " +
                $"(character id={characterId})");
        }

        int threshold = ParseOptionalIntInRange(
            traitEl, "threshold", 0, 0, int.MaxValue, characterId);
        int usesPerTurn = ParseOptionalIntInRange(
            traitEl, "usesPerTurn", 0, 0, int.MaxValue, characterId);
        string? description = GetOptionalString(traitEl, "description");

        if (!traitEl.TryGetProperty("effects", out var effectsEl) ||
            effectsEl.ValueKind != JsonValueKind.Array)
        {
            throw new CharacterJsonException(
                $"trait '{id}' effects must be an array (character id={characterId})");
        }

        var effects = new List<CardEffect>();
        foreach (var effectEl in effectsEl.EnumerateArray())
        {
            effects.Add(CardEffectParser.ParseEffect(
                effectEl,
                message => new CharacterJsonException(
                    $"{message} (character id={characterId}, trait id={id})")));
        }
        if (effects.Count == 0)
        {
            throw new CharacterJsonException(
                $"trait '{id}' effects must not be empty (character id={characterId})");
        }

        return new CharacterTraitDefinition(
            id, name, trigger, effects, threshold, usesPerTurn, description);
    }

    private static CharacterClass ParseCharacterClass(JsonElement root, string characterId)
    {
        if (!root.TryGetProperty("characterClass", out var value) ||
            value.ValueKind == JsonValueKind.Null)
            return CharacterClass.Striker;
        if (value.ValueKind != JsonValueKind.String ||
            !Enum.TryParse<CharacterClass>(value.GetString(), ignoreCase: false, out var result) ||
            !Enum.IsDefined(result))
        {
            throw new CharacterJsonException(
                $"characterClass contains an unknown value (character id={characterId})");
        }
        return result;
    }

    private static IReadOnlyList<CharacterSkillDefinition> ParseSkills(JsonElement root, string characterId)
    {
        if (!root.TryGetProperty("skills", out var skillsEl) ||
            skillsEl.ValueKind == JsonValueKind.Null)
            return Array.Empty<CharacterSkillDefinition>();
        if (skillsEl.ValueKind != JsonValueKind.Array)
            throw new CharacterJsonException($"skills must be an array (character id={characterId})");

        var result = new List<CharacterSkillDefinition>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var skillEl in skillsEl.EnumerateArray())
        {
            if (skillEl.ValueKind != JsonValueKind.Object)
                throw new CharacterJsonException($"skills entries must be objects (character id={characterId})");
            string id = GetRequiredString(skillEl, "id", characterId);
            string name = GetRequiredString(skillEl, "name", characterId);
            if (!ids.Add(id))
                throw new CharacterJsonException($"skill id '{id}' is duplicated (character id={characterId})");

            int energy = ParseOptionalIntInRange(skillEl, "energyCost", 0, 0, int.MaxValue, characterId);
            int gauge = ParseOptionalIntInRange(skillEl, "comboGaugeCost", 0, 0, int.MaxValue, characterId);
            bool once = skillEl.TryGetProperty("oncePerTurn", out var onceEl) &&
                        onceEl.ValueKind == JsonValueKind.True;
            int usesPerTurn = ParseOptionalIntInRange(
                skillEl, "usesPerTurn", once ? 1 : 0, 0, int.MaxValue, characterId);
            bool requiresHand = skillEl.TryGetProperty("requiresCardsInHand", out var handEl) &&
                                handEl.ValueKind == JsonValueKind.True;
            string? description = skillEl.TryGetProperty("description", out var descEl) &&
                                  descEl.ValueKind == JsonValueKind.String
                ? descEl.GetString()
                : null;
            if (!skillEl.TryGetProperty("effects", out var effectsEl) ||
                effectsEl.ValueKind != JsonValueKind.Array)
                throw new CharacterJsonException($"skill '{id}' effects must be an array (character id={characterId})");

            var effects = new List<CardEffect>();
            foreach (var effectEl in effectsEl.EnumerateArray())
                effects.Add(CardEffectParser.ParseEffect(
                    effectEl,
                    message => new CharacterJsonException($"{message} (character id={characterId}, skill id={id})")));
            if (effects.Count == 0)
                throw new CharacterJsonException($"skill '{id}' effects must not be empty (character id={characterId})");

            IReadOnlyList<string>? keywords = null;
            if (skillEl.TryGetProperty("keywords", out var keywordsEl))
            {
                if (keywordsEl.ValueKind != JsonValueKind.Array)
                    throw new CharacterJsonException($"skill '{id}' keywords must be an array (character id={characterId})");
                var keywordList = new List<string>();
                foreach (var keywordEl in keywordsEl.EnumerateArray())
                {
                    if (keywordEl.ValueKind != JsonValueKind.String ||
                        string.IsNullOrWhiteSpace(keywordEl.GetString()))
                        throw new CharacterJsonException($"skill '{id}' keywords must contain non-empty strings (character id={characterId})");
                    keywordList.Add(keywordEl.GetString()!);
                }
                keywords = keywordList;
            }

            result.Add(new CharacterSkillDefinition(
                id, name, new ResourceCost(energy, gauge), once, effects,
                description, requiresHand, keywords, usesPerTurn));
        }
        return result;
    }

    private static string GetRequiredString(JsonElement element, string key, string characterId)
    {
        if (!element.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String)
            throw new CharacterJsonException($"'{key}' must be a string (character id={characterId})");
        return value.GetString()!;
    }

    private static string GetRequiredNonEmptyString(
        JsonElement element,
        string key,
        string characterId,
        string owner)
    {
        if (!element.TryGetProperty(key, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new CharacterJsonException(
                $"'{key}' must be a non-empty string " +
                $"(character id={characterId}, {owner})");
        }

        return value.GetString()!;
    }

    private static string? GetOptionalString(JsonElement element, string key)
    {
        if (!element.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new CharacterJsonException($"'{key}' must be a string.");
        return value.GetString();
    }

    private static int ParseOptionalIntInRange(
        JsonElement el, string key, int defaultValue, int min, int max, string? id)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind == JsonValueKind.Null)
            return defaultValue;
        if (v.ValueKind != JsonValueKind.Number)
            throw new CharacterJsonException(
                $"\"{key}\" は数値である必要があります (character id={id})。");
        int n = v.GetInt32();
        if (n < min || n > max)
            throw new CharacterJsonException(
                $"\"{key}\" の値 {n} は {min}..{max} の範囲外です (character id={id})。");
        return n;
    }
}
