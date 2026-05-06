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
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(catalog);
        return Math.Max(0, @base + SumPassiveBonus("energyPerTurnBonus", s, catalog));
    }

    public static int ApplyCardsDrawnPerTurnBonus(int @base, RunState s, DataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(catalog);
        return Math.Max(0, @base + SumPassiveBonus("cardsDrawnPerTurnBonus", s, catalog));
    }

    public static int ApplyRewardCardChoicesBonus(int @base, RunState s, DataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(catalog);
        return Math.Max(1, @base + SumPassiveBonus("rewardCardChoicesBonus", s, catalog));
    }

    public static int ApplyPassiveRestHealBonus(int @base, RunState s, DataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(catalog);
        return @base + SumPassiveBonus("restHealBonus", s, catalog);
    }

    // ---- ×系 modifier (delta from 100, additive stacking) ----

    public static int ApplyGoldRewardMultiplier(int @base, RunState s, DataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(catalog);
        int delta = SumPassiveMultiplierDelta("goldRewardMultiplier", s, catalog);
        return Math.Max(0, (int)((long)@base * (100 + delta) / 100));
    }

    public static int ApplyShopPriceMultiplier(int @base, RunState s, DataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(catalog);
        int delta = SumPassiveMultiplierDelta("shopPriceMultiplier", s, catalog);
        return Math.Max(1, (int)((long)@base * (100 + delta) / 100));
    }

    // ---- Capability flag ----

    public static bool HasPassiveCapability(string action, RunState s, DataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(catalog);
        return SumPassiveBonus(action, s, catalog) > 0;
    }

    // ---- Unknown 重み補正 (1 action `unknownTileWeightDelta` + name で 6 種別を分岐、床 0) ----

    public static ImmutableDictionary<TileKind, double> ApplyUnknownWeightDeltas(
        UnknownResolutionConfig config, RunState s, DataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(catalog);

        // unknownTileWeightDelta action で eff.Name == tile kind name のものを集計
        var deltaMap = new System.Collections.Generic.Dictionary<TileKind, int>
        {
            [TileKind.Enemy]    = SumPassiveBonusByName("unknownTileWeightDelta", "enemy",    s, catalog),
            [TileKind.Elite]    = SumPassiveBonusByName("unknownTileWeightDelta", "elite",    s, catalog),
            [TileKind.Merchant] = SumPassiveBonusByName("unknownTileWeightDelta", "merchant", s, catalog),
            [TileKind.Rest]     = SumPassiveBonusByName("unknownTileWeightDelta", "rest",     s, catalog),
            [TileKind.Treasure] = SumPassiveBonusByName("unknownTileWeightDelta", "treasure", s, catalog),
            [TileKind.Event]    = SumPassiveBonusByName("unknownTileWeightDelta", "event",    s, catalog),
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

    /// <summary>action + name (tile kind 等) の組み合わせで集計する。
    /// Phase 10.6.B Q3 改修: unknownTileWeightDelta 等 1 action × 多 sub-kind の用途。</summary>
    private static int SumPassiveBonusByName(string action, string name, RunState s, DataCatalog catalog)
    {
        int sum = 0;
        foreach (var id in s.Relics)
        {
            if (!catalog.TryGetRelic(id, out var def)) continue;
            if (!def.Implemented) continue;
            foreach (var eff in def.Effects)
            {
                if (eff.Trigger != "Passive") continue;
                if (eff.Action != action) continue;
                if (eff.Name != name) continue;
                sum += eff.Amount;
            }
        }
        return sum;
    }

    // SumPassiveBonus と算法は同一だが、将来 multiplier の合成方式が
    // additive (現状) から compound に変わる場合の hook ポイントとして API を分離。
    private static int SumPassiveMultiplierDelta(string action, RunState s, DataCatalog catalog)
        => SumPassiveBonus(action, s, catalog);
}
