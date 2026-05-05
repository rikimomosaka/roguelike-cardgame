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
            TileKind.Unknown => ResolveUnknownAndDispatch(state, currentRow, data, rng),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    /// <summary>
    /// Phase 10.6.B T8: Unknown タイルを lazy 解決してから種別に応じた処理を委譲する。
    /// cache hit なら既存の解決値を使い、未解決なら relic modifier を適用して抽選。
    /// 全 weight 0 の fallback は元 config の weights を使う (defensive)。
    /// </summary>
    private static RunState ResolveUnknownAndDispatch(
        RunState state, int currentRow, DataCatalog data, IRng rng)
    {
        int nodeId = state.CurrentNodeId;

        // cache hit: 既に解決済みならその値で再 dispatch
        if (state.UnknownResolutions.TryGetValue(nodeId, out var cached))
            return Resolve(state, cached, currentRow, data, rng);

        // 未解決 → modifier 適用後に lazy resolve
        // UnknownConfig が null の場合 (テスト用途等) は MapGenerationConfigLoader からロード
        var config = data.UnknownConfig ?? MapGenerationConfigLoader.LoadAct1().UnknownResolutionWeights;
        var weights = PassiveModifiers.ApplyUnknownWeightDeltas(config, state, data);

        // 全 weight 0 fallback: 元 config に戻す (defensive)
        if (weights.Values.Sum() <= 0)
            weights = config.Weights;

        var resolved = UnknownResolver.ResolveOne(weights, rng);

        // 解決結果を cache に追記
        var newState = state with {
            UnknownResolutions = state.UnknownResolutions.SetItem(nodeId, resolved)
        };

        // resolved kind で再 dispatch (1 段再帰、resolved は Unknown 以外なので無限ループなし)
        return Resolve(newState, resolved, currentRow, data, rng);
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
        return RewardActions.AssignReward(s, reward, newRng, data);
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
