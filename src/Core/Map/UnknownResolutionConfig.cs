using System.Collections.Immutable;
using System.Linq;

namespace RoguelikeCardGame.Core.Map;

/// <summary>
/// Unknown マスを具体 TileKind に解決するための重み設定。
/// 抽選先は Enemy / Elite / Merchant / Rest / Treasure のみ許可。
/// </summary>
public sealed record UnknownResolutionConfig(
    ImmutableDictionary<TileKind, double> Weights)
{
    /// <summary>不変条件を検査する。違反があれば理由文字列、問題なければ null。</summary>
    public string? Validate()
    {
        if (Weights.IsEmpty) return "UnknownResolutionConfig.Weights must not be empty";
        foreach (var kv in Weights)
        {
            if (kv.Key is TileKind.Unknown or TileKind.Start or TileKind.Boss)
                return $"UnknownResolutionConfig.Weights cannot contain {kv.Key}";
            if (kv.Value < 0)
                return $"UnknownResolutionConfig.Weights[{kv.Key}] must be >= 0 (got {kv.Value})";
        }
        if (Weights.Values.Sum() <= 0) return "UnknownResolutionConfig.Weights sum must be > 0";
        return null;
    }
}
