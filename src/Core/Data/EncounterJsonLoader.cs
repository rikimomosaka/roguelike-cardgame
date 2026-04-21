using System;
using System.Collections.Generic;
using System.Text.Json;
using RoguelikeCardGame.Core.Enemy;

namespace RoguelikeCardGame.Core.Data;

public sealed class EncounterJsonException : Exception
{
    public EncounterJsonException(string message) : base(message) { }
    public EncounterJsonException(string message, Exception inner) : base(message, inner) { }
}

public static class EncounterJsonLoader
{
    public static EncounterDefinition Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new EncounterJsonException("encounter JSON のパース失敗", ex); }

        using (doc)
        {
            var root = doc.RootElement;
            string id = root.GetProperty("id").GetString()!;
            int act = root.GetProperty("act").GetInt32();
            string tierStr = root.GetProperty("tier").GetString()!;
            EnemyTier tier = tierStr switch
            {
                "Weak" => EnemyTier.Weak,
                "Strong" => EnemyTier.Strong,
                "Elite" => EnemyTier.Elite,
                "Boss" => EnemyTier.Boss,
                _ => throw new EncounterJsonException($"tier \"{tierStr}\" は無効 (id={id})"),
            };

            var enemyIds = new List<string>();
            if (!root.TryGetProperty("enemyIds", out var arr) || arr.ValueKind != JsonValueKind.Array)
                throw new EncounterJsonException($"enemyIds は配列必須 (id={id})");
            foreach (var e in arr.EnumerateArray())
                enemyIds.Add(e.GetString() ?? throw new EncounterJsonException($"enemyIds 要素が string でない (id={id})"));

            if (enemyIds.Count == 0)
                throw new EncounterJsonException($"enemyIds が空 (id={id})");

            return new EncounterDefinition(id, new EnemyPool(act, tier), enemyIds);
        }
    }
}
