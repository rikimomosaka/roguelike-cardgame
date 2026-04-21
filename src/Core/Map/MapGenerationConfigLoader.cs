using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using RoguelikeCardGame.Core.Json;

namespace RoguelikeCardGame.Core.Map;

/// <summary>Map config JSON のパース失敗を表す例外。</summary>
public sealed class MapGenerationConfigException : Exception
{
    public MapGenerationConfigException(string message) : base(message) { }
    public MapGenerationConfigException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>埋め込み JSON から <see cref="MapGenerationConfig"/> をロードする。</summary>
public static class MapGenerationConfigLoader
{
    private const string Act1ResourceName = "RoguelikeCardGame.Core.Map.Config.map-act1.json";

    public static MapGenerationConfig LoadAct1()
    {
        var asm = typeof(MapGenerationConfigLoader).Assembly;
        using var stream = asm.GetManifestResourceStream(Act1ResourceName)
            ?? throw new MapGenerationConfigException(
                $"Embedded resource not found: {Act1ResourceName}");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static MapGenerationConfig Parse(string json)
    {
        Dto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<Dto>(json, JsonOptions.Default);
        }
        catch (JsonException ex)
        {
            throw new MapGenerationConfigException("map-config JSON のパースに失敗しました。", ex);
        }
        if (dto is null) throw new MapGenerationConfigException("map-config JSON が null でした。");

        try
        {
            return dto.ToConfig();
        }
        catch (Exception ex) when (ex is not MapGenerationConfigException)
        {
            throw new MapGenerationConfigException("map-config の値変換に失敗しました。", ex);
        }
    }

    private sealed record Dto(
        int RowCount,
        int ColumnCount,
        int RowNodeCountMin,
        int RowNodeCountMax,
        EdgeDto EdgeWeights,
        TileDistDto TileDistribution,
        FixedRowDto[] FixedRows,
        ExclusionDto[] RowKindExclusions,
        PathDto PathConstraints,
        int MaxRegenerationAttempts)
    {
        public MapGenerationConfig ToConfig() => new(
            RowCount,
            ColumnCount,
            RowNodeCountMin,
            RowNodeCountMax,
            new EdgeCountWeights(EdgeWeights.Weight1, EdgeWeights.Weight2, EdgeWeights.Weight3),
            new TileDistributionRule(
                BaseWeights: TileDistribution.BaseWeights.ToImmutableDictionary(),
                MinPerMap: TileDistribution.MinPerMap.ToImmutableDictionary(),
                MaxPerMap: TileDistribution.MaxPerMap.ToImmutableDictionary()),
            FixedRows.Select(f => new FixedRowRule(f.Row, f.Kind)).ToImmutableArray(),
            RowKindExclusions.Select(x => new RowKindExclusion(x.Row, x.ExcludedKind)).ToImmutableArray(),
            new PathConstraintRule(
                PerPathCount: PathConstraints.PerPathCount.ToImmutableDictionary(
                    kv => kv.Key,
                    kv => new IntRange(kv.Value.Min, kv.Value.Max)),
                MinEliteRow: PathConstraints.MinEliteRow,
                ForbiddenConsecutive: PathConstraints.ForbiddenConsecutive
                    .Select(p => new TileKindPair(p.First, p.Second))
                    .ToImmutableArray()),
            MaxRegenerationAttempts);
    }

    private sealed record EdgeDto(double Weight1, double Weight2, double Weight3);
    private sealed record TileDistDto(
        System.Collections.Generic.Dictionary<TileKind, double> BaseWeights,
        System.Collections.Generic.Dictionary<TileKind, int> MinPerMap,
        System.Collections.Generic.Dictionary<TileKind, int> MaxPerMap);
    private sealed record FixedRowDto(int Row, TileKind Kind);
    private sealed record ExclusionDto(int Row, TileKind ExcludedKind);
    private sealed record PathDto(
        System.Collections.Generic.Dictionary<TileKind, RangeDto> PerPathCount,
        int MinEliteRow,
        ConsecutiveDto[] ForbiddenConsecutive);
    private sealed record RangeDto(int Min, int Max);
    private sealed record ConsecutiveDto(TileKind First, TileKind Second);
}
