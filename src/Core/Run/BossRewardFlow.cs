using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Rewards;

namespace RoguelikeCardGame.Core.Run;

public static class BossRewardFlow
{
    public static RewardState? GenerateBossReward(
        RunState state, DataCatalog catalog, IRng rng)
    {
        if (state.CurrentAct >= RunConstants.MaxAct) return null;

        var tableId = $"act{state.CurrentAct}";
        if (!catalog.RewardTables.TryGetValue(tableId, out var table))
            table = catalog.RewardTables["act1"];

        var (reward, _) = RewardGenerator.Generate(
            new RewardContext.FromEnemy(new EnemyPool(state.CurrentAct, EnemyTier.Boss)),
            state.RewardRngState,
            ImmutableArray.Create("strike", "defend"),
            table, catalog, rng);
        return reward with { IsBossReward = true };
    }
}
