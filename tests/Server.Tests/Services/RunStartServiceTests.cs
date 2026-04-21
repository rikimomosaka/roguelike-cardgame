using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Services;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Services;

public class RunStartServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 4, 22, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task StartAsync_RetriesSeedOnMapGenerationException()
    {
        var config = MapGenerationConfigLoader.LoadAct1();
        // 最初の 3 回は強制失敗、4 回目はスタブマップを返すジェネレータ。実ジェネレータの
        // seed 運によるテスト揺らぎを避けるため、スタブマップで retry ロジックだけを検証する。
        var gen = new FlakeyGenerator(failFirst: 3, success: StubMap());
        var saves = new InMemorySaveRepository();
        int callCount = 0;
        var service = new RunStartService(gen, config, saves,
            now: () => FixedNow,
            seedSource: () => { callCount++; return 1000 + callCount; });

        var (state, map) = await service.StartAsync("alice", CancellationToken.None);

        Assert.Equal(4, gen.Calls);
        Assert.Equal(StubMap(), map);
        Assert.Equal(1004UL, state.RngSeed);
        Assert.NotNull(await saves.TryLoadAsync("alice", CancellationToken.None));
    }

    private static DungeonMap StubMap()
    {
        var nodes = System.Collections.Immutable.ImmutableArray.Create(
            new MapNode(0, 0, 0, TileKind.Start,
                System.Collections.Immutable.ImmutableArray.Create(1)),
            new MapNode(1, 1, 0, TileKind.Boss,
                System.Collections.Immutable.ImmutableArray<int>.Empty));
        return new DungeonMap(nodes, 0, 1);
    }

    [Fact]
    public async Task StartAsync_GivesUpAfterMaxAttempts()
    {
        var config = MapGenerationConfigLoader.LoadAct1();
        var gen = new AlwaysFailingGenerator();
        var saves = new InMemorySaveRepository();
        var service = new RunStartService(gen, config, saves,
            now: () => FixedNow,
            seedSource: () => 42);

        await Assert.ThrowsAsync<MapGenerationException>(() =>
            service.StartAsync("bob", CancellationToken.None));
        Assert.Equal(10, gen.Calls);
        Assert.Null(await saves.TryLoadAsync("bob", CancellationToken.None));
    }

    private sealed class FlakeyGenerator : IDungeonMapGenerator
    {
        private readonly int _failFirst;
        private readonly DungeonMap _success;
        public int Calls { get; private set; }

        public FlakeyGenerator(int failFirst, DungeonMap success)
        {
            _failFirst = failFirst;
            _success = success;
        }

        public DungeonMap Generate(IRng rng, MapGenerationConfig config)
        {
            Calls++;
            if (Calls <= _failFirst)
                throw new MapGenerationException(config.MaxRegenerationAttempts, "test-forced");
            return _success;
        }
    }

    private sealed class AlwaysFailingGenerator : IDungeonMapGenerator
    {
        public int Calls { get; private set; }

        public DungeonMap Generate(IRng rng, MapGenerationConfig config)
        {
            Calls++;
            throw new MapGenerationException(config.MaxRegenerationAttempts, "test-forced");
        }
    }

    private sealed class InMemorySaveRepository : ISaveRepository
    {
        private readonly Dictionary<string, RunState> _store = new();

        public Task SaveAsync(string accountId, RunState state, CancellationToken ct)
        {
            _store[accountId] = state;
            return Task.CompletedTask;
        }

        public Task<RunState?> TryLoadAsync(string accountId, CancellationToken ct)
            => Task.FromResult(_store.TryGetValue(accountId, out var s) ? s : null);

        public Task DeleteAsync(string accountId, CancellationToken ct)
        {
            _store.Remove(accountId);
            return Task.CompletedTask;
        }
    }
}
