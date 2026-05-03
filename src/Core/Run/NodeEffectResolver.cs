using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Bestiary;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Events;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Merchant;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Rewards;

namespace RoguelikeCardGame.Core.Run;

/// <summary>TileKind に応じて RunState を遷移させるルータ。各マス種別のロジックは個別モジュールに委譲する。</summary>
public static class NodeEffectResolver
{
    public static RunState Resolve(
        RunState state, TileKind kind, int currentRow, DataCatalog data, IRng rng)
    {
        // 前のマスの未完了状態をクリア（次のマスに入った時点で閉じる）
        state = state with
        {
            ActiveMerchant = null,
            ActiveEvent = null,
            ActiveRestPending = false,
            ActiveRestCompleted = false,
            ActiveActStartRelicChoice = null,
        };

        var table = data.RewardTables["act1"];
        return kind switch
        {
            TileKind.Start => state with {
                ActiveActStartRelicChoice = ActStartActions.GenerateChoices(state, state.CurrentAct, data, rng),
            },
            TileKind.Enemy => BattlePlaceholder.Start(state,
                RouteEnemyPool(table, state.CurrentAct, currentRow), data, rng),
            TileKind.Elite => BattlePlaceholder.Start(state,
                new EnemyPool(state.CurrentAct, EnemyTier.Elite), data, rng),
            TileKind.Boss => BattlePlaceholder.Start(state,
                new EnemyPool(state.CurrentAct, EnemyTier.Boss), data, rng),
            TileKind.Rest => NonBattleRelicEffects.ApplyOnEnterRestSite(
                state with { ActiveRestPending = true }, data),
            TileKind.Merchant => StartMerchant(state, data, rng),
            TileKind.Treasure => StartTreasure(state, table, data, rng),
            TileKind.Event => StartEvent(state, data, rng),
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

    private static RunState StartTreasure(RunState s, RewardTable table, DataCatalog data, IRng rng)
    {
        var owned = ImmutableArray.CreateRange(s.Relics);
        var (reward, newRng) = RewardGenerator.GenerateTreasure(s.RewardRngState, owned, table, data, rng);
        var s1 = s with { ActiveReward = reward, RewardRngState = newRng };
        return NonBattleRelicEffects.ApplyOnRewardGenerated(s1, data);
    }

    private static RunState StartEvent(RunState s, DataCatalog data, IRng rng)
    {
        var pool = ImmutableArray.CreateRange(data.Events.Values);
        var def = EventPool.Pick(pool, s.CurrentAct, s, rng);
        var inst = new EventInstance(def.Id, def.Choices);
        return s with { ActiveEvent = inst };
    }

    private static RunState StartMerchant(RunState s, DataCatalog data, IRng rng)
    {
        if (data.MerchantPrices is null)
            throw new InvalidOperationException("DataCatalog.MerchantPrices is not configured");
        var inv = MerchantInventoryGenerator.Generate(data, data.MerchantPrices, s, rng);
        var next = s with { ActiveMerchant = inv };
        next = BestiaryTracker.NoteCardsSeen(next, inv.Cards.Select(o => o.Id));
        return NonBattleRelicEffects.ApplyOnEnterShop(next, data);
    }
}
