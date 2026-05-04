using System;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Rewards;

/// <summary>
/// Phase 10.6.B で導入される reward flow control の集約点。
/// 5 reward 生成サイト (Treasure / Boss / Event / Battle 終了 / Run 勝利) で共通の
/// 「ActiveReward 設定 + goldRewardMultiplier 適用 + OnRewardGenerated 発火」を
/// 1 関数に集約 (Phase 10.6.A T8 で 5 ヶ所に inline されていた logic を整理)。
/// </summary>
public static class RewardActions
{
    public static RunState AssignReward(
        RunState s, RewardState reward, RewardRngState newRng, DataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(reward);
        ArgumentNullException.ThrowIfNull(catalog);

        // Phase 10.6.B: goldRewardMultiplier を適用
        var goldAdjusted = PassiveModifiers.ApplyGoldRewardMultiplier(reward.Gold, s, catalog);
        var rewardWithGold = reward with { Gold = goldAdjusted };

        var s1 = s with { ActiveReward = rewardWithGold, RewardRngState = newRng };
        return NonBattleRelicEffects.ApplyOnRewardGenerated(s1, catalog);
    }

    /// <summary>
    /// Phase 10.6.B T7: 報酬カード選択肢を 1 reward につき 1 度リロールする。
    /// Relic が "rewardRerollAvailable" Passive capability を持つ場合のみ有効。
    /// CardStatus == Pending かつ RerollUsed == false のときのみ実行可能。
    /// </summary>
    public static RunState Reroll(
        RunState s, DataCatalog catalog, IRng rng,
        EnemyPool sourcePool, RewardTable table)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(table);

        var r = s.ActiveReward
            ?? throw new InvalidOperationException("No ActiveReward to reroll");
        if (r.CardStatus != CardRewardStatus.Pending)
            throw new InvalidOperationException("Card already resolved, cannot reroll");
        if (r.RerollUsed)
            throw new InvalidOperationException("Reroll already used for this reward");
        if (!PassiveModifiers.HasPassiveCapability("rewardRerollAvailable", s, catalog))
            throw new InvalidOperationException("No relic grants reward reroll");

        var newPicks = RewardGenerator.RegenerateCardChoicesForReward(
            sourcePool, s.RewardRngState, ImmutableArray<string>.Empty,
            table, catalog, rng, s);

        return s with
        {
            ActiveReward = r with
            {
                CardChoices = newPicks,
                RerollUsed = true,
            }
        };
    }
}
