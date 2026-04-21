using System;
using System.Collections.Generic;
using System.Text.Json;

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

            return new CharacterDefinition(id, name, maxHp, gold, slots, deck);
        }
    }
}
