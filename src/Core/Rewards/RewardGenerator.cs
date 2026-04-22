using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Enemy;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Rewards;

public static class RewardGenerator
{
    public static (RewardState reward, RewardRngState newRng) Generate(
        RewardContext context,
        RewardRngState rngState,
        ImmutableArray<string> cardExclusions,
        RewardTable table,
        DataCatalog data,
        IRng rng)
    {
        return context switch
        {
            RewardContext.FromEnemy fe => GenerateFromEnemy(fe.Pool, rngState, cardExclusions, table, data, rng),
            RewardContext.FromNonBattle nb when nb.Kind == NonBattleRewardKind.Treasure
                => GenerateTreasure(rngState, ImmutableArray<string>.Empty, table, data, rng),
            RewardContext.FromNonBattle nb
                => GenerateFromNonBattleEvent(nb.Kind, rngState, table, rng),
            _ => throw new ArgumentOutOfRangeException(nameof(context))
        };
    }

    public static (RewardState, RewardRngState) GenerateTreasure(
        RewardRngState rngState,
        ImmutableArray<string> ownedRelics,
        RewardTable table,
        DataCatalog data,
        IRng rng)
    {
        var pool = data.Relics.Keys
            .Where(id => !ownedRelics.Contains(id))
            .OrderBy(id => id)
            .ToArray();
        string? relic = pool.Length == 0 ? null : pool[rng.NextInt(0, pool.Length)];
        var reward = new RewardState(
            Gold: 0, GoldClaimed: true,
            PotionId: null, PotionClaimed: true,
            CardChoices: ImmutableArray<string>.Empty,
            CardStatus: CardRewardStatus.Claimed,
            RelicId: relic,
            RelicClaimed: relic is null);
        return (reward, rngState);
    }

    private static (RewardState, RewardRngState) GenerateFromNonBattleEvent(
        NonBattleRewardKind kind, RewardRngState rngState, RewardTable table, IRng rng)
    {
        string key = "event";
        var entry = table.NonBattle[key];
        int gold = entry.GoldMin + rng.NextInt(0, entry.GoldMax - entry.GoldMin + 1);
        var reward = new RewardState(
            Gold: gold, GoldClaimed: false,
            PotionId: null, PotionClaimed: true,
            CardChoices: ImmutableArray<string>.Empty,
            CardStatus: CardRewardStatus.Claimed);
        return (reward, rngState);
    }

    private static (RewardState, RewardRngState) GenerateFromEnemy(
        EnemyPool pool, RewardRngState rngState,
        ImmutableArray<string> excl, RewardTable table, DataCatalog data, IRng rng)
    {
        var entry = table.Pools[pool.Tier];

        int gold = entry.GoldMin + rng.NextInt(0, entry.GoldMax - entry.GoldMin + 1);

        string? potionId = null;
        var newRng = rngState;
        int potionBase = entry.PotionBasePercent;
        if (potionBase == 100) potionId = PickRandomPotion(data, rng);
        else if (potionBase == 0) { }
        else
        {
            int chance = rngState.PotionChancePercent;
            if (rng.NextInt(0, 100) < chance)
            {
                potionId = PickRandomPotion(data, rng);
                newRng = newRng with
                {
                    PotionChancePercent = Math.Max(table.PotionDynamic.Min,
                        chance - table.PotionDynamic.Step)
                };
            }
            else
            {
                newRng = newRng with
                {
                    PotionChancePercent = Math.Min(table.PotionDynamic.Max,
                        chance + table.PotionDynamic.Step)
                };
            }
        }

        int commonPct = entry.CommonPercent;
        int rarePct = entry.RarePercent;
        int epicPct = entry.EpicPercent;
        int bonus = rngState.RareChanceBonusPercent;

        int rareFinal = Math.Min(100, rarePct + bonus);
        int take = rareFinal - rarePct;
        int commonFinal = Math.Max(0, commonPct - take);
        int epicFinal = Math.Max(0, 100 - rareFinal - commonFinal);

        var picks = new List<string>();
        var seen = new HashSet<string>();
        while (picks.Count < 3)
        {
            var r = rng.NextInt(0, 100);
            CardRarity rarity;
            if (r < commonFinal) rarity = CardRarity.Common;
            else if (r < commonFinal + rareFinal) rarity = CardRarity.Rare;
            else rarity = CardRarity.Epic;

            var pool2 = data.Cards.Values
                .Where(c => c.Rarity == rarity && c.Id.StartsWith("reward_"))
                .Where(c => !excl.Contains(c.Id) && !seen.Contains(c.Id))
                .Select(c => c.Id)
                .ToList();
            if (pool2.Count == 0) continue;
            var pick = pool2[rng.NextInt(0, pool2.Count)];
            picks.Add(pick);
            seen.Add(pick);
        }

        bool hasRare = picks.Any(id => data.Cards[id].Rarity == CardRarity.Rare);
        newRng = newRng with
        {
            RareChanceBonusPercent = hasRare ? 0 : rngState.RareChanceBonusPercent + table.EpicChance.PerBattleIncrement
        };

        var reward = new RewardState(
            Gold: gold, GoldClaimed: false,
            PotionId: potionId, PotionClaimed: potionId is null,
            CardChoices: picks.ToImmutableArray(),
            CardStatus: CardRewardStatus.Pending);
        return (reward, newRng);
    }

    private static string PickRandomPotion(DataCatalog data, IRng rng)
    {
        var ids = data.Potions.Keys.OrderBy(s => s).ToArray();
        return ids[rng.NextInt(0, ids.Length)];
    }
}
