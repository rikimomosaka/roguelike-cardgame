using System;
using System.Threading;
using System.Threading.Tasks;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;

namespace RoguelikeCardGame.Server.Services;

/// <summary>新規ソロランを構築し永続化するサービス。seed → map → unknown 解決 → RunState → save。</summary>
public sealed class RunStartService
{
    private readonly IDungeonMapGenerator _generator;
    private readonly MapGenerationConfig _mapConfig;
    private readonly ISaveRepository _saves;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<int> _seedSource;

    public RunStartService(
        IDungeonMapGenerator generator,
        MapGenerationConfig mapConfig,
        ISaveRepository saves,
        Func<DateTimeOffset>? now = null,
        Func<int>? seedSource = null)
    {
        _generator = generator;
        _mapConfig = mapConfig;
        _saves = saves;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _seedSource = seedSource ?? (() => System.Random.Shared.Next());
    }

    /// <summary>seed を最大 <see cref="MaxSeedAttempts"/> 回振り直して <see cref="MapGenerationException"/> を吸収する。
    /// DungeonMapGenerator は与えられた RNG で <see cref="MapGenerationConfig.MaxRegenerationAttempts"/> 回内部試行するが、
    /// 制約が厳しい seed ではその範囲で成功できないことがあるため、外側でも seed 自体を振り直す。</summary>
    private const int MaxSeedAttempts = 10;

    public async Task<(RunState state, DungeonMap map)> StartAsync(string accountId, CancellationToken ct)
    {
        int seed = 0;
        DungeonMap? map = null;
        MapGenerationException? last = null;
        for (int attempt = 0; attempt < MaxSeedAttempts; attempt++)
        {
            seed = _seedSource();
            try
            {
                map = _generator.Generate(new SystemRng(seed), _mapConfig);
                break;
            }
            catch (MapGenerationException ex)
            {
                last = ex;
            }
        }
        if (map is null) throw last!;
        var resolutions = UnknownResolver.ResolveAll(
            map, _mapConfig.UnknownResolutionWeights, new SystemRng(unchecked(seed + 1)));
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var state = RunState.NewSoloRun(
            catalog,
            rngSeed: unchecked((ulong)(uint)seed),
            startNodeId: map.StartNodeId,
            unknownResolutions: resolutions,
            nowUtc: _now());
        await _saves.SaveAsync(accountId, state, ct);
        return (state, map);
    }

    /// <summary>
    /// 保存済み seed から map の構造のみを再生成して返す（move / current 用）。
    /// Unknown の解決結果は含まれない — それは <see cref="RunState.UnknownResolutions"/> に永続化されている。
    /// </summary>
    public DungeonMap RehydrateMap(ulong rngSeed)
    {
        int seed = unchecked((int)(uint)rngSeed);
        return _generator.Generate(new SystemRng(seed), _mapConfig);
    }
}
