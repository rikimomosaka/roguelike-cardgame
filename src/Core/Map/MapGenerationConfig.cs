using System.Collections.Immutable;
using System.Linq;

namespace RoguelikeCardGame.Core.Map;

/// <summary>
/// ダンジョンマップ生成の全設定。JSON から deserialize して <see cref="IDungeonMapGenerator.Generate"/> に渡す。
/// </summary>
public sealed record MapGenerationConfig(
    int RowCount,
    int ColumnCount,
    int RowNodeCountMin,
    int RowNodeCountMax,
    EdgeCountWeights EdgeWeights,
    TileDistributionRule TileDistribution,
    ImmutableArray<FixedRowRule> FixedRows,
    ImmutableArray<RowKindExclusion> RowKindExclusions,
    PathConstraintRule PathConstraints,
    int MaxRegenerationAttempts,
    UnknownResolutionConfig UnknownResolutionWeights)
{
    /// <summary>
    /// 構造的な不変条件を検査する。違反があれば人間可読な理由文字列、問題なければ null を返す。
    /// 例外型に依存したくないので throw はせず、呼び出し側 (loader など) が包んで投げる。
    /// </summary>
    public string? Validate()
    {
        if (RowCount < 1) return $"RowCount must be >= 1 (got {RowCount})";
        if (ColumnCount < 1) return $"ColumnCount must be >= 1 (got {ColumnCount})";
        if (RowNodeCountMin < 1) return $"RowNodeCountMin must be >= 1 (got {RowNodeCountMin})";
        if (RowNodeCountMax < RowNodeCountMin)
            return $"RowNodeCountMax ({RowNodeCountMax}) must be >= RowNodeCountMin ({RowNodeCountMin})";
        if (RowNodeCountMax > ColumnCount)
            return $"RowNodeCountMax ({RowNodeCountMax}) must be <= ColumnCount ({ColumnCount})";
        if (MaxRegenerationAttempts < 1)
            return $"MaxRegenerationAttempts must be >= 1 (got {MaxRegenerationAttempts})";

        double edgeTotal = EdgeWeights.Weight1 + EdgeWeights.Weight2 + EdgeWeights.Weight3;
        if (EdgeWeights.Weight1 < 0 || EdgeWeights.Weight2 < 0 || EdgeWeights.Weight3 < 0)
            return "EdgeWeights must be non-negative";
        if (edgeTotal <= 0) return "EdgeWeights sum must be > 0";

        if (TileDistribution.BaseWeights.Any(kv => kv.Value < 0))
            return "TileDistribution.BaseWeights must be non-negative";
        if (TileDistribution.BaseWeights.Values.Sum() <= 0)
            return "TileDistribution.BaseWeights sum must be > 0";

        foreach (var kv in TileDistribution.MinPerMap)
            if (kv.Value < 0) return $"TileDistribution.MinPerMap[{kv.Key}] must be >= 0 (got {kv.Value})";
        foreach (var kv in TileDistribution.MaxPerMap)
            if (kv.Value < 0) return $"TileDistribution.MaxPerMap[{kv.Key}] must be >= 0 (got {kv.Value})";
        foreach (var kv in TileDistribution.MinPerMap)
        {
            if (TileDistribution.MaxPerMap.TryGetValue(kv.Key, out int max) && kv.Value > max)
                return $"TileDistribution.MinPerMap[{kv.Key}]={kv.Value} exceeds MaxPerMap={max}";
        }

        foreach (var rule in FixedRows)
            if (rule.Row < 1 || rule.Row > RowCount)
                return $"FixedRows[{rule.Row}] out of range [1..{RowCount}]";
        foreach (var rule in RowKindExclusions)
            if (rule.Row < 1 || rule.Row > RowCount)
                return $"RowKindExclusions[{rule.Row}] out of range [1..{RowCount}]";

        if (PathConstraints.MinEliteRow < 1)
            return $"PathConstraints.MinEliteRow must be >= 1 (got {PathConstraints.MinEliteRow})";
        foreach (var kv in PathConstraints.PerPathCount)
        {
            if (kv.Value.Min < 0) return $"PathConstraints.PerPathCount[{kv.Key}].Min must be >= 0 (got {kv.Value.Min})";
            if (kv.Value.Max < kv.Value.Min)
                return $"PathConstraints.PerPathCount[{kv.Key}].Max ({kv.Value.Max}) must be >= Min ({kv.Value.Min})";
        }
        var unknownInvalid = UnknownResolutionWeights.Validate();
        if (unknownInvalid is not null) return unknownInvalid;

        return null;
    }
}
