using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using RoguelikeCardGame.Core.Battle;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Enemy;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;
using ActMapSeedHelper = RoguelikeCardGame.Core.Run.ActMapSeed;

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
            var act1Seed = unchecked((int)(uint)ActMapSeedHelper.Derive((ulong)(uint)seed, 1));
            try
            {
                map = _generator.Generate(new SystemRng(act1Seed), _mapConfig);
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

        // seed+1 は UnknownResolver が使用。Encounter 用に seed+2..+5 を割り当てる。
        var queueWeak = EncounterQueue.Initialize(
            new EnemyPool(Act: 1, Tier: EnemyTier.Weak), catalog, new SystemRng(unchecked(seed + 2)));
        var queueStrong = EncounterQueue.Initialize(
            new EnemyPool(Act: 1, Tier: EnemyTier.Strong), catalog, new SystemRng(unchecked(seed + 3)));
        var queueElite = EncounterQueue.Initialize(
            new EnemyPool(Act: 1, Tier: EnemyTier.Elite), catalog, new SystemRng(unchecked(seed + 4)));
        var queueBoss = EncounterQueue.Initialize(
            new EnemyPool(Act: 1, Tier: EnemyTier.Boss), catalog, new SystemRng(unchecked(seed + 5)));

        var state = RunState.NewSoloRun(
            catalog,
            rngSeed: unchecked((ulong)(uint)seed),
            startNodeId: map.StartNodeId,
            unknownResolutions: resolutions,
            encounterQueueWeak: queueWeak,
            encounterQueueStrong: queueStrong,
            encounterQueueElite: queueElite,
            encounterQueueBoss: queueBoss,
            nowUtc: _now());

        // Phase 7: 各アクト開始時にレリック 3 択を提示する。act 1 の初期選択もここで生成する。
        var choice = ActStartActions.GenerateChoices(
            state, act: 1, catalog, new SystemRng(unchecked(seed + 6)));
        state = state with { ActiveActStartRelicChoice = choice };

        await _saves.SaveAsync(accountId, state, ct);
        return (state, map);
    }

    /// <summary>
    /// 保存済み seed から map の構造のみを再生成して返す（move / current 用）。
    /// Unknown の解決結果は含まれない — それは <see cref="RunState.UnknownResolutions"/> に永続化されている。
    /// </summary>
    /// <param name="rngSeed">ランの生の RNG seed（RunState.RngSeed）。</param>
    /// <param name="act">act 番号（1 始まり）。ActMapSeed.Derive で per-act seed に変換する。</param>
    public DungeonMap RehydrateMap(ulong rngSeed, int act = 1)
    {
        var derived = ActMapSeedHelper.Derive(rngSeed, act);
        int seed = unchecked((int)(uint)derived);
        return _generator.Generate(new SystemRng(seed), _mapConfig);
    }

    /// <summary>
    /// アクト遷移時に新マップの Unknown ノードを解決する。
    /// 同じ (rngSeed, act, map) に対して決定的に同じ結果を返す。
    /// </summary>
    public ImmutableDictionary<int, TileKind> ResolveUnknownsForAct(
        DungeonMap map, ulong rngSeed, int act)
    {
        var derived = ActMapSeedHelper.Derive(rngSeed, act);
        int seed = unchecked((int)(uint)derived);
        var rng = new SystemRng(unchecked(seed ^ 0x11E50));
        return UnknownResolver.ResolveAll(map, _mapConfig.UnknownResolutionWeights, rng);
    }
}
