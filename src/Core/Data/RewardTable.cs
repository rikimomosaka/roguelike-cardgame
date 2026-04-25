using System.Collections.Generic;
using RoguelikeCardGame.Core.Battle.Definitions;

namespace RoguelikeCardGame.Core.Data;

public sealed record RewardTable(
    string Id,
    IReadOnlyDictionary<EnemyTier, RewardPoolEntry> Pools,
    IReadOnlyDictionary<string, NonBattleEntry> NonBattle, // "event", "treasure"
    PotionDynamicConfig PotionDynamic,
    EpicChanceConfig EpicChance,
    EnemyPoolRoutingConfig EnemyPoolRouting);

public sealed record RewardPoolEntry(
    int GoldMin,
    int GoldMax,
    int PotionBasePercent,      // Elite=100, Boss=0, その他=40
    int CommonPercent,
    int RarePercent,
    int EpicPercent);           // 合計 100 になる想定

public sealed record NonBattleEntry(int GoldMin, int GoldMax);

public sealed record PotionDynamicConfig(int InitialPercent, int Step, int Min, int Max);

public sealed record EpicChanceConfig(int InitialBonus, int PerBattleIncrement);

public sealed record EnemyPoolRoutingConfig(int WeakRowsThreshold);
