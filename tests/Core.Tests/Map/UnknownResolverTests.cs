using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Map;

public class UnknownResolverTests
{
    private static UnknownResolutionConfig SampleConfig() =>
        new(ImmutableDictionary<TileKind, double>.Empty
            .Add(TileKind.Enemy, 48)
            .Add(TileKind.Merchant, 24)
            .Add(TileKind.Rest, 24)
            .Add(TileKind.Treasure, 4));

    private static DungeonMap GenerateMapWithUnknowns()
    {
        var cfg = MapGenerationConfigLoader.LoadAct1();
        return new DungeonMapGenerator().Generate(new SystemRng(58), cfg);
    }

    [Fact]
    public void ResolveAll_SameSeed_SameResult()
    {
        var map = GenerateMapWithUnknowns();
        var cfg = SampleConfig();
        var a = UnknownResolver.ResolveAll(map, cfg, new SystemRng(123));
        var b = UnknownResolver.ResolveAll(map, cfg, new SystemRng(123));
        Assert.Equal(a.Count, b.Count);
        foreach (var kv in a) Assert.Equal(kv.Value, b[kv.Key]);
    }

    [Fact]
    public void ResolveAll_OnlyUnknownNodesPresent()
    {
        var map = GenerateMapWithUnknowns();
        var cfg = SampleConfig();
        var result = UnknownResolver.ResolveAll(map, cfg, new SystemRng(123));
        foreach (var nodeId in result.Keys)
            Assert.Equal(TileKind.Unknown, map.GetNode(nodeId).Kind);
    }

    [Fact]
    public void ResolveAll_ZeroWeightKindNeverSelected()
    {
        var map = GenerateMapWithUnknowns();
        var cfg = new UnknownResolutionConfig(ImmutableDictionary<TileKind, double>.Empty
            .Add(TileKind.Enemy, 1)
            .Add(TileKind.Merchant, 0));
        var result = UnknownResolver.ResolveAll(map, cfg, new SystemRng(123));
        Assert.All(result.Values, v => Assert.Equal(TileKind.Enemy, v));
    }

    [Fact]
    public void ResolveAll_ForbiddenKindInWeights_Throws()
    {
        var map = GenerateMapWithUnknowns();
        var badCfg = new UnknownResolutionConfig(
            ImmutableDictionary<TileKind, double>.Empty.Add(TileKind.Boss, 1));
        Assert.Throws<MapGenerationConfigException>(
            () => UnknownResolver.ResolveAll(map, badCfg, new SystemRng(1)));
    }

    [Fact]
    public void ResolveAll_NullArgs_Throw()
    {
        var map = GenerateMapWithUnknowns();
        var cfg = SampleConfig();
        Assert.Throws<ArgumentNullException>(() => UnknownResolver.ResolveAll(null!, cfg, new SystemRng(1)));
        Assert.Throws<ArgumentNullException>(() => UnknownResolver.ResolveAll(map, null!, new SystemRng(1)));
        Assert.Throws<ArgumentNullException>(() => UnknownResolver.ResolveAll(map, cfg, null!));
    }

    [Fact]
    public void Event_IsAllowedInWeights()
    {
        var cfg = new UnknownResolutionConfig(
            System.Collections.Immutable.ImmutableDictionary<TileKind, double>.Empty
                .Add(TileKind.Event, 1.0));
        Assert.Null(cfg.Validate());
    }
}
