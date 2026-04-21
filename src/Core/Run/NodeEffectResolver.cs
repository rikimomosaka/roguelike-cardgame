using System;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Enemy;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Rewards;

namespace RoguelikeCardGame.Core.Run;

/// <summary>
/// TileKind に応じて RunState を遷移させる。戦闘マスは BattlePlaceholder.Start、
/// Treasure は RewardGenerator で ActiveReward を立て、Rest は HP 全回復、
/// Merchant/Start は副作用なし。
/// </summary>
public static class NodeEffectResolver
{
    public static RunState Resolve(
        RunState state,
        TileKind kind,
        int currentRow,
        DataCatalog data,
        IRng rng)
    {
        var table = data.RewardTables["act1"];
        return kind switch
        {
            TileKind.Start => state,
            TileKind.Enemy => BattlePlaceholder.Start(state,
                RouteEnemyPool(table, state.CurrentAct, currentRow), data, rng),
            TileKind.Elite => BattlePlaceholder.Start(state,
                new EnemyPool(state.CurrentAct, EnemyTier.Elite), data, rng),
            TileKind.Boss => BattlePlaceholder.Start(state,
                new EnemyPool(state.CurrentAct, EnemyTier.Boss), data, rng),
            TileKind.Rest => state with { CurrentHp = state.MaxHp },
            TileKind.Merchant => state,
            TileKind.Treasure => ApplyNonBattleReward(state, NonBattleRewardKind.Treasure, table, data, rng),
            TileKind.Unknown => throw new ArgumentException("Unknown tile should be pre-resolved"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static EnemyPool RouteEnemyPool(RewardTable table, int act, int row)
    {
        var tier = row < table.EnemyPoolRouting.WeakRowsThreshold
            ? EnemyTier.Weak
            : EnemyTier.Strong;
        return new EnemyPool(act, tier);
    }

    private static RunState ApplyNonBattleReward(RunState s, NonBattleRewardKind kind,
        RewardTable table, DataCatalog data, IRng rng)
    {
        var (reward, newRng) = RewardGenerator.Generate(
            new RewardContext.FromNonBattle(kind),
            s.RewardRngState,
            ImmutableArray.Create("strike", "defend"),
            table, data, rng);
        return s with { ActiveReward = reward, RewardRngState = newRng };
    }
}
