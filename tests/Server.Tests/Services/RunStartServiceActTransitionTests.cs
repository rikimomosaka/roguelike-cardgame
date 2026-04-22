using System.Linq;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Server.Abstractions;
using RoguelikeCardGame.Server.Services;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Services;

public class RunStartServiceActTransitionTests
{
    private static RunStartService BuildService()
    {
        var config = MapGenerationConfigLoader.LoadAct1();
        var generator = new DungeonMapGenerator();
        var saves = new NullSaveRepository();
        return new RunStartService(generator, config, saves);
    }

    [Fact]
    public void RehydrateMap_DifferentAct_YieldsDifferentMaps()
    {
        var svc = BuildService();
        var map1 = svc.RehydrateMap(rngSeed: 42UL, act: 1);
        var map2 = svc.RehydrateMap(rngSeed: 42UL, act: 2);
        // ActMapSeed.Derive produces a different seed per act → different map structure.
        // Compare node count or boss node id as a proxy for different generation.
        bool structureDiffers =
            map1.Nodes.Length != map2.Nodes.Length ||
            map1.BossNodeId != map2.BossNodeId ||
            !map1.Nodes.SequenceEqual(map2.Nodes);
        Assert.True(structureDiffers, "act 1 と act 2 で異なる seed が使われているため、map 構造が異なるはず。");
    }

    [Fact]
    public void RehydrateMap_SameActSameSeed_YieldsSameMap()
    {
        var svc = BuildService();
        var map1 = svc.RehydrateMap(rngSeed: 99UL, act: 1);
        var map2 = svc.RehydrateMap(rngSeed: 99UL, act: 1);
        Assert.Equal(map1, map2);
    }

    private sealed class NullSaveRepository : ISaveRepository
    {
        public System.Threading.Tasks.Task SaveAsync(string accountId, RoguelikeCardGame.Core.Run.RunState state, System.Threading.CancellationToken ct)
            => System.Threading.Tasks.Task.CompletedTask;

        public System.Threading.Tasks.Task<RoguelikeCardGame.Core.Run.RunState?> TryLoadAsync(string accountId, System.Threading.CancellationToken ct)
            => System.Threading.Tasks.Task.FromResult<RoguelikeCardGame.Core.Run.RunState?>(null);

        public System.Threading.Tasks.Task DeleteAsync(string accountId, System.Threading.CancellationToken ct)
            => System.Threading.Tasks.Task.CompletedTask;
    }
}
