using System;
using RoguelikeCardGame.Core.Data;
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
}
