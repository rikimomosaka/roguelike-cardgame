using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
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
            RewardContext.FromNonBattle
                => GenerateFromNonBattleEvent(rngState, table, rng),
            _ => throw new ArgumentOutOfRangeException(nameof(context))
        };
    }

    /// <summary>
    /// 宝箱マス用の報酬を生成する。必ず Gold（テーブル設定）とレリック 1 本のコンボで入る。
    /// 所有済みレリックを除外した候補からランダム選択。
    /// 全レリック所有済みの場合は <c>RelicId = null</c> / <c>RelicClaimed = true</c>（レリック取得不要）。
    /// </summary>
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
        var entry = table.NonBattle["treasure"];
        int goldRange = entry.GoldMax - entry.GoldMin + 1;
        int gold = goldRange <= 1 ? entry.GoldMin : entry.GoldMin + rng.NextInt(0, goldRange);
        var reward = new RewardState(
            Gold: gold, GoldClaimed: false,
            PotionId: null, PotionClaimed: true,
            CardChoices: ImmutableArray<string>.Empty,
            CardStatus: CardRewardStatus.Claimed,
            RelicId: relic,
            RelicClaimed: relic is null);
        return (reward, rngState);
    }

    private static (RewardState, RewardRngState) GenerateFromNonBattleEvent(
        RewardRngState rngState, RewardTable table, IRng rng)
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
        if (potionBase == 100)
        {
            // Elite: always drop, do not touch dynamic chance
            potionId = PickRandomPotion(data, rng);
        }
        else if (potionBase == 0)
        {
            // Boss: never drop, do not touch dynamic chance
        }
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

            // Why: c.Rarity == rarity (Common/Rare/Epic) で Token は元々除外されるが、
            // 将来 rarity 選択ロジックが変わっても token カードが紛れ込まないよう
            // 明示的に Token を除外する防御的フィルタを追加 (Phase 10.5.G)。
            var pool2 = data.Cards.Values
                .Where(c => c.Rarity != CardRarity.Token)
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
