using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Rewards;

namespace RoguelikeCardGame.Core.Run;

public static class BossRewardFlow
{
    /// <summary>
    /// ボス報酬を生成して <see cref="RunState.ActiveReward"/> に設定し、
    /// <c>OnRewardGenerated</c> トリガーを発火した RunState を返す。
    /// 最終アクトの場合は ActiveReward を設定せずそのまま返す。
    /// </summary>
    public static RunState Resolve(RunState state, DataCatalog catalog, IRng rng)
    {
        var reward = GenerateBossReward(state, catalog, rng);
        if (reward is null) return state;
        return RewardActions.AssignReward(state, reward, state.RewardRngState, catalog);
    }

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
            table, catalog, rng, state);
        return reward with { IsBossReward = true };
    }
}
