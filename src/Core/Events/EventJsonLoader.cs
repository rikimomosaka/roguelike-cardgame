using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Events;

public sealed class EventJsonException : Exception
{
    public EventJsonException(string message) : base(message) { }
    public EventJsonException(string message, Exception inner) : base(message, inner) { }
}

public static class EventJsonLoader
{
    public static EventDefinition Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new EventJsonException("event JSON のパースに失敗しました。", ex); }

        using (doc)
        {
            var root = doc.RootElement;
            string id = GetString(root, "id");
            string name = GetString(root, "name");
            string desc = GetString(root, "description");
            if (!root.TryGetProperty("choices", out var choicesEl) || choicesEl.ValueKind != JsonValueKind.Array)
                throw new EventJsonException($"event \"{id}\" に choices 配列がありません。");
            if (choicesEl.GetArrayLength() < 1)
                throw new EventJsonException($"event \"{id}\" の choices は 1 要素以上必要です。");

            var choices = ImmutableArray.CreateBuilder<EventChoice>();
            foreach (var ch in choicesEl.EnumerateArray())
            {
                string label = GetString(ch, "label");
                EventCondition? cond = null;
                if (ch.TryGetProperty("condition", out var condEl) && condEl.ValueKind == JsonValueKind.Object)
                    cond = ParseCondition(condEl, id);
                var effects = ParseEffects(ch, id);
                choices.Add(new EventChoice(label, cond, effects));
            }
            return new EventDefinition(id, name, desc, choices.ToImmutable());
        }
    }

    private static EventCondition ParseCondition(JsonElement el, string eventId)
    {
        string type = GetString(el, "type");
        return type switch
        {
            "minGold" => new EventCondition.MinGold(GetInt(el, "amount")),
            "minHp" => new EventCondition.MinHp(GetInt(el, "amount")),
            _ => throw new EventJsonException($"event \"{eventId}\" の condition.type \"{type}\" は無効。")
        };
    }

    private static ImmutableArray<EventEffect> ParseEffects(JsonElement choiceEl, string eventId)
    {
        if (!choiceEl.TryGetProperty("effects", out var effs) || effs.ValueKind != JsonValueKind.Array)
            return ImmutableArray<EventEffect>.Empty;
        var list = new List<EventEffect>();
        foreach (var e in effs.EnumerateArray())
        {
            string type = GetString(e, "type");
            EventEffect effect = type switch
            {
                "gainGold" => new EventEffect.GainGold(GetInt(e, "amount")),
                "payGold" => new EventEffect.PayGold(GetInt(e, "amount")),
                "heal" => new EventEffect.Heal(GetInt(e, "amount")),
                "takeDamage" => new EventEffect.TakeDamage(GetInt(e, "amount")),
                "gainMaxHp" => new EventEffect.GainMaxHp(GetInt(e, "amount")),
                "loseMaxHp" => new EventEffect.LoseMaxHp(GetInt(e, "amount")),
                "gainRelicRandom" => new EventEffect.GainRelicRandom(ParseRarity(GetInt(e, "rarity"), eventId)),
                "grantCardReward" => new EventEffect.GrantCardReward(),
                _ => throw new EventJsonException($"event \"{eventId}\" の effect.type \"{type}\" は無効。")
            };
            list.Add(effect);
        }
        return list.ToImmutableArray();
    }

    private static CardRarity ParseRarity(int raw, string eventId)
    {
        if (!Enum.IsDefined(typeof(CardRarity), raw))
            throw new EventJsonException($"event \"{eventId}\" の rarity {raw} は無効。");
        return (CardRarity)raw;
    }

    private static string GetString(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String)
            throw new EventJsonException($"必須フィールド \"{key}\" (string) がありません。");
        return v.GetString()!;
    }

    private static int GetInt(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.Number)
            throw new EventJsonException($"必須フィールド \"{key}\" (number) がありません。");
        return v.GetInt32();
    }
}
