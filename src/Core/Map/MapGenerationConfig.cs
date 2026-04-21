using System.Collections.Immutable;

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
    int MaxRegenerationAttempts);
