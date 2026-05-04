using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Definitions;

namespace RoguelikeCardGame.Core.Rewards;

public enum CardRewardStatus { Pending, Claimed, Skipped }

public enum NonBattleRewardKind { Event, Treasure }

public abstract record RewardContext
{
    public sealed record FromEnemy(EnemyPool Pool) : RewardContext;
    public sealed record FromNonBattle(NonBattleRewardKind Kind) : RewardContext;
}

public sealed record RewardState(
    int Gold,
    bool GoldClaimed,
    string? PotionId,
    bool PotionClaimed,
    ImmutableArray<string> CardChoices,
    CardRewardStatus CardStatus,
    string? RelicId = null,
    bool RelicClaimed = true,
    bool IsBossReward = false,
    bool RerollUsed = false);  // Phase 10.6.B T7

public sealed record RewardRngState(
    int PotionChancePercent,
    int RareChanceBonusPercent);
