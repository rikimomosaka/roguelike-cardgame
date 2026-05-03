using System;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Relics;

/// <summary>
/// Phase 10.6.B で導入される passive modifier system の集約 façade。
/// 全 modifier (加算 / ×系 / capability / unknown weight delta) を
/// `Trigger == "Passive"` の relic effect から評価する純関数群。
/// </summary>
/// <remarks>
/// 評価方式: lazy (毎回 `RunState.Relics` をループ集計、caching なし)。
/// Phase 10.6.A で確立された `NonBattleRelicEffects.ApplyPassiveRestHealBonus`
/// パターンを generalize し、複数 modifier action に対応。
/// </remarks>
public static class PassiveModifiers
{
    // ---- 加算系 modifier ----

    public static int ApplyEnergyPerTurnBonus(int @base, RunState s, DataCatalog catalog)
        => Math.Max(0, @base + SumPassiveBonus("energyPerTurnBonus", s, catalog));

    public static int ApplyCardsDrawnPerTurnBonus(int @base, RunState s, DataCatalog catalog)
        => Math.Max(0, @base + SumPassiveBonus("cardsDrawnPerTurnBonus", s, catalog));

    public static int ApplyRewardCardChoicesBonus(int @base, RunState s, DataCatalog catalog)
        => Math.Max(1, @base + SumPassiveBonus("rewardCardChoicesBonus", s, catalog));

    public static int ApplyPassiveRestHealBonus(int @base, RunState s, DataCatalog catalog)
        => @base + SumPassiveBonus("restHealBonus", s, catalog);

    // ---- ×系 modifier (delta from 100, additive stacking) ----

    public static int ApplyGoldRewardMultiplier(int @base, RunState s, DataCatalog catalog)
    {
        int delta = SumPassiveMultiplierDelta("goldRewardMultiplier", s, catalog);
        return Math.Max(0, (int)((long)@base * (100 + delta) / 100));
    }

    public static int ApplyShopPriceMultiplier(int @base, RunState s, DataCatalog catalog)
    {
        int delta = SumPassiveMultiplierDelta("shopPriceMultiplier", s, catalog);
        return Math.Max(1, (int)((long)@base * (100 + delta) / 100));
    }

    // ---- Capability flag ----

    public static bool HasPassiveCapability(string action, RunState s, DataCatalog catalog)
        => SumPassiveBonus(action, s, catalog) > 0;

    // ---- Unknown 重み補正 (5 種別を 1 関数で処理、床 0) ----

    public static ImmutableDictionary<TileKind, double> ApplyUnknownWeightDeltas(
        UnknownResolutionConfig config, RunState s, DataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(catalog);

        var deltaMap = new System.Collections.Generic.Dictionary<TileKind, int>
        {
            [TileKind.Enemy]    = SumPassiveBonus("unknownEnemyWeightDelta",    s, catalog),
            [TileKind.Elite]    = SumPassiveBonus("unknownEliteWeightDelta",    s, catalog),
            [TileKind.Merchant] = SumPassiveBonus("unknownMerchantWeightDelta", s, catalog),
            [TileKind.Rest]     = SumPassiveBonus("unknownRestWeightDelta",     s, catalog),
            [TileKind.Treasure] = SumPassiveBonus("unknownTreasureWeightDelta", s, catalog),
        };

        var builder = ImmutableDictionary.CreateBuilder<TileKind, double>();
        foreach (var kv in config.Weights)
        {
            int delta = deltaMap.GetValueOrDefault(kv.Key, 0);
            builder.Add(kv.Key, Math.Max(0.0, kv.Value + delta));
        }
        return builder.ToImmutable();
    }

    // ---- 内部 helper ----

    private static int SumPassiveBonus(string action, RunState s, DataCatalog catalog)
    {
        int sum = 0;
        foreach (var id in s.Relics)
        {
            if (!catalog.TryGetRelic(id, out var def)) continue;
            if (!def.Implemented) continue;
            foreach (var eff in def.Effects)
            {
                if (eff.Trigger != "Passive") continue;
                if (eff.Action == action) sum += eff.Amount;
            }
        }
        return sum;
    }

    private static int SumPassiveMultiplierDelta(string action, RunState s, DataCatalog catalog)
        => SumPassiveBonus(action, s, catalog);
}
