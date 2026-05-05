using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Map;

/// <summary>
/// Map 内の全 Unknown ノードについて、重み付きランダムで具体 kind を抽選する。
/// ノードを Id 昇順で走査するため、同じ IRng 状態で呼べば同じ結果になる（決定的）。
/// </summary>
public static class UnknownResolver
{
    public static ImmutableDictionary<int, TileKind> ResolveAll(
        DungeonMap map, UnknownResolutionConfig config, IRng rng)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(rng);

        var invalid = config.Validate();
        if (invalid is not null)
            throw new MapGenerationConfigException($"UnknownResolutionConfig 不変条件違反: {invalid}");

        var entries = config.Weights.Where(kv => kv.Value > 0).ToArray();
        double totalWeight = entries.Sum(kv => kv.Value);

        var builder = ImmutableDictionary.CreateBuilder<int, TileKind>();
        foreach (var node in map.Nodes.OrderBy(n => n.Id))
        {
            if (node.Kind != TileKind.Unknown) continue;
            double r = rng.NextDouble() * totalWeight;
            double acc = 0;
            TileKind picked = entries[^1].Key;
            foreach (var kv in entries)
            {
                acc += kv.Value;
                if (r < acc) { picked = kv.Key; break; }
            }
            builder.Add(node.Id, picked);
        }
        return builder.ToImmutable();
    }

    /// <summary>
    /// Phase 10.6.B T8: 1 ノードだけを与えられた重みで決定的に抽選する。
    /// NodeEffectResolver から lazy resolve 時に呼ばれる。
    /// 全 weight が 0 の場合は MapGenerationConfigException を投げる
    /// (caller 側で fallback 処理)。
    /// </summary>
    public static TileKind ResolveOne(ImmutableDictionary<TileKind, double> weights, IRng rng)
    {
        System.ArgumentNullException.ThrowIfNull(weights);
        System.ArgumentNullException.ThrowIfNull(rng);

        var entries = weights.Where(kv => kv.Value > 0).ToArray();
        double totalWeight = entries.Sum(kv => kv.Value);
        if (totalWeight <= 0)
            throw new MapGenerationConfigException("ResolveOne: all weights are zero");

        double r = rng.NextDouble() * totalWeight;
        double acc = 0;
        foreach (var kv in entries)
        {
            acc += kv.Value;
            if (r < acc) return kv.Key;
        }
        return entries[^1].Key;
    }
}
