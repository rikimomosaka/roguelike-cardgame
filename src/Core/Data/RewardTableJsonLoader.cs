using System;
using System.Collections.Generic;
using System.Text.Json;
using RoguelikeCardGame.Core.Battle.Definitions;

namespace RoguelikeCardGame.Core.Data;

public sealed class RewardTableJsonException : Exception
{
    public RewardTableJsonException(string message) : base(message) { }
    public RewardTableJsonException(string message, Exception inner) : base(message, inner) { }
}

public static class RewardTableJsonLoader
{
    public static RewardTable Parse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new RewardTableJsonException("reward-table JSON のパース失敗", ex); }

        using (doc)
        {
            var r = doc.RootElement;
            string id = r.GetProperty("id").GetString()!;

            var pools = new Dictionary<EnemyTier, RewardPoolEntry>();
            var poolsEl = r.GetProperty("pools");
            foreach (var kv in poolsEl.EnumerateObject())
            {
                EnemyTier tier = kv.Name switch
                {
                    "weak" => EnemyTier.Weak,
                    "strong" => EnemyTier.Strong,
                    "elite" => EnemyTier.Elite,
                    "boss" => EnemyTier.Boss,
                    _ => throw new RewardTableJsonException($"pools.\"{kv.Name}\" は無効"),
                };
                var p = kv.Value;
                var gold = p.GetProperty("gold");
                int goldMin = gold[0].GetInt32();
                int goldMax = gold[1].GetInt32();
                int potBase = p.GetProperty("potionBase").GetInt32();
                var dist = p.GetProperty("rarityDist");
                int common = dist.GetProperty("common").GetInt32();
                int rare = dist.GetProperty("rare").GetInt32();
                int epic = dist.GetProperty("epic").GetInt32();
                if (common + rare + epic != 100)
                    throw new RewardTableJsonException($"rarityDist sum != 100 at pools.{kv.Name}");
                pools[tier] = new RewardPoolEntry(goldMin, goldMax, potBase, common, rare, epic);
            }

            var nonBattle = new Dictionary<string, NonBattleEntry>();
            var nbEl = r.GetProperty("nonBattle");
            foreach (var kv in nbEl.EnumerateObject())
            {
                var g = kv.Value.GetProperty("gold");
                nonBattle[kv.Name] = new NonBattleEntry(g[0].GetInt32(), g[1].GetInt32());
            }

            var pd = r.GetProperty("potionDynamic");
            var pdCfg = new PotionDynamicConfig(
                pd.GetProperty("initialPercent").GetInt32(),
                pd.GetProperty("step").GetInt32(),
                pd.GetProperty("min").GetInt32(),
                pd.GetProperty("max").GetInt32());

            var ec = r.GetProperty("epicChance");
            var ecCfg = new EpicChanceConfig(
                ec.GetProperty("initialBonus").GetInt32(),
                ec.GetProperty("perBattleIncrement").GetInt32());

            var ep = r.GetProperty("enemyPoolRouting");
            var epCfg = new EnemyPoolRoutingConfig(ep.GetProperty("weakRowsThreshold").GetInt32());

            return new RewardTable(id, pools, nonBattle, pdCfg, ecCfg, epCfg);
        }
    }
}
