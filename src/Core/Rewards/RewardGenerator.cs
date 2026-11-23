using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Rewards;

public static class RewardGenerator
{
    public static (RewardState reward, RewardRngState newRng) Generate(
        RewardContext context,
        RewardRngState rngState,
        ImmutableArray<string> cardExclusions,
        RewardTable table,
        DataCatalog data,
        IRng rng,
        RunState runState)  // Phase 10.6.B T5
    {
        return context switch
        {
            RewardContext.FromEnemy fe => GenerateFromEnemy(fe.Pool, rngState, cardExclusions, table, data, rng, runState),
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
        var pool = RelicPoolHelper.GetNormalAcquisitionRelics(data)
            .Select(relic => relic.Id)
            .Where(id => !ownedRelics.Contains(id))
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
        ImmutableArray<string> excl, RewardTable table, DataCatalog data, IRng rng,
        RunState runState)  // Phase 10.6.B T5
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

        // Phase 10.6.B T5: card 抽選を切り出し helper 経由に
        var picks = RegenerateCardChoicesForReward(pool, newRng, excl, table, data, rng, runState);

        bool hasRare = picks.Any(id => data.Cards[id].Rarity == CardRarity.Rare);
        newRng = newRng with
        {
            RareChanceBonusPercent = hasRare ? 0 : rngState.RareChanceBonusPercent + table.EpicChance.PerBattleIncrement
        };

        var reward = new RewardState(
            Gold: gold, GoldClaimed: false,
            PotionId: potionId, PotionClaimed: potionId is null,
            CardChoices: picks,
            CardStatus: picks.IsEmpty ? CardRewardStatus.Claimed : CardRewardStatus.Pending);
        return (reward, newRng);
    }

    /// <summary>
    /// Phase 10.6.B T5/T7: reward の card choices だけを (再) 抽選する純関数。
    /// T5 では GenerateFromEnemy の card 抽選部分として利用、T7 (reroll) では既存 reward の
    /// card choices を別 RNG で再抽選するエントリポイント。
    /// rewardCardChoicesBonus modifier を考慮した枚数で抽選。
    /// </summary>
    public static ImmutableArray<string> RegenerateCardChoicesForReward(
        EnemyPool pool, RewardRngState rngState,
        ImmutableArray<string> exclusions,
        RewardTable table, DataCatalog data, IRng rng,
        RunState runState) =>
        RegenerateCardChoices(
            pool,
            rngState,
            exclusions,
            table,
            data,
            rng,
            runState,
            CardPoolRules.IsClassRewardFor);

    /// <summary>
    /// Generates card choices from the shared Universal reward pool. This is used
    /// by event rewards and intentionally does not mutate <see cref="RewardRngState"/>.
    /// </summary>
    public static ImmutableArray<string> RegenerateUniversalCardChoices(
        EnemyPool pool, RewardRngState rngState,
        ImmutableArray<string> exclusions,
        RewardTable table, DataCatalog data, IRng rng,
        RunState runState) =>
        RegenerateCardChoices(
            pool,
            rngState,
            exclusions,
            table,
            data,
            rng,
            runState,
            CardPoolRules.IsUniversalRewardFor);

    private static ImmutableArray<string> RegenerateCardChoices(
        EnemyPool pool, RewardRngState rngState,
        ImmutableArray<string> exclusions,
        RewardTable table, DataCatalog data, IRng rng,
        RunState runState,
        Func<CardDefinition, CharacterDefinition, bool> isEligible)
    {
        if (!data.Characters.TryGetValue(runState.CharacterId, out var character))
            throw new InvalidOperationException(
                $"Character \"{runState.CharacterId}\" is not configured in the data catalog");

        int targetCount = PassiveModifiers.ApplyRewardCardChoicesBonus(3, runState, data);
        var entry = table.Pools[pool.Tier];
        int commonPct = entry.CommonPercent;
        int rarePct = entry.RarePercent;
        int bonus = rngState.RareChanceBonusPercent;
        int rareFinal = Math.Min(100, rarePct + bonus);
        int take = rareFinal - rarePct;
        int commonFinal = Math.Max(0, commonPct - take);

        var available = data.Cards.Values
            .Where(c => c.Rarity is CardRarity.Common or CardRarity.Rare or CardRarity.Epic)
            .Where(c => isEligible(c, character))
            .Where(c => !exclusions.Contains(c.Id))
            .OrderBy(c => c.Id, StringComparer.Ordinal)
            .GroupBy(c => c.Rarity)
            .ToDictionary(
                group => group.Key,
                group => group.Select(c => c.Id).ToList());

        int commonWeight = Math.Clamp(commonFinal, 0, 100);
        int rareWeight = Math.Clamp(rareFinal, 0, 100 - commonWeight);
        var rarityWeights = new Dictionary<CardRarity, int>
        {
            [CardRarity.Common] = commonWeight,
            [CardRarity.Rare] = rareWeight,
            [CardRarity.Epic] = 100 - commonWeight - rareWeight,
        };
        var picks = new List<string>(targetCount);
        while (picks.Count < targetCount)
        {
            // Only roll among rarities that can still produce a card.  Retrying an
            // empty rarity forever used to hang reward generation when exclusions,
            // a rarity-specific reward, or a choice-count bonus exhausted the pool.
            var weightedRarities = rarityWeights
                .Where(pair => pair.Value > 0
                    && available.TryGetValue(pair.Key, out var cards)
                    && cards.Count > 0)
                .OrderBy(pair => pair.Key)
                .ToArray();
            int totalWeight = weightedRarities.Sum(pair => pair.Value);
            if (totalWeight <= 0) break;

            int rarityRoll = rng.NextInt(0, totalWeight);
            CardRarity rarity = weightedRarities[^1].Key;
            foreach (var pair in weightedRarities)
            {
                if (rarityRoll < pair.Value)
                {
                    rarity = pair.Key;
                    break;
                }
                rarityRoll -= pair.Value;
            }

            // Why: c.Rarity == rarity (Common/Rare/Epic) で Token は元々除外されるが、
            // 将来 rarity 選択ロジックが変わっても token カードが紛れ込まないよう
            // 明示的に Token を除外する防御的フィルタを追加 (Phase 10.5.G)。
            var pool2 = available[rarity];
            int pickIndex = rng.NextInt(0, pool2.Count);
            var pick = pool2[pickIndex];
            pool2.RemoveAt(pickIndex);
            picks.Add(pick);
        }
        return picks.ToImmutableArray();
    }

    private static string PickRandomPotion(DataCatalog data, IRng rng)
    {
        var ids = data.Potions.Keys.OrderBy(s => s).ToArray();
        return ids[rng.NextInt(0, ids.Length)];
    }
}
