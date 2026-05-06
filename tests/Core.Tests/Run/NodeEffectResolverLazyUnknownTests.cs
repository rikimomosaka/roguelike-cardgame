using System.Collections.Immutable;
using System.Collections.Generic;
using RoguelikeCardGame.Core.Battle;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class NodeEffectResolverLazyUnknownTests
{
    private static readonly DataCatalog BaseCatalog = EmbeddedDataLoader.LoadCatalog();

    /// <summary>
    /// CurrentNodeId=42 に設定した RunState。
    /// EncounterQueue は全 tier で初期化済み (lazy resolve が Enemy/Elite に落ちても動く)。
    /// </summary>
    private static RunState SampleStateAtUnknownNode(DataCatalog catalog)
    {
        var rng = new SystemRng(1);
        return RunState.NewSoloRun(
            catalog, 1UL, 0,
            ImmutableDictionary<int, TileKind>.Empty,
            encounterQueueWeak:   EncounterQueue.Initialize(new EnemyPool(1, EnemyTier.Weak),   catalog, rng),
            encounterQueueStrong: EncounterQueue.Initialize(new EnemyPool(1, EnemyTier.Strong), catalog, rng),
            encounterQueueElite:  EncounterQueue.Initialize(new EnemyPool(1, EnemyTier.Elite),  catalog, rng),
            encounterQueueBoss:   EncounterQueue.Initialize(new EnemyPool(1, EnemyTier.Boss),   catalog, rng),
            new System.DateTimeOffset(2026, 5, 4, 0, 0, 0, System.TimeSpan.Zero)
        ) with { CurrentNodeId = 42 };
    }

    [Fact]
    public void Resolve_Unknown_LazyResolves_AndCachesResult()
    {
        var state = SampleStateAtUnknownNode(BaseCatalog);
        var s1 = NodeEffectResolver.Resolve(state, TileKind.Unknown, currentRow: 5, BaseCatalog, new SequentialRng(1UL));

        Assert.True(s1.UnknownResolutions.ContainsKey(42));
        var resolved = s1.UnknownResolutions[42];
        Assert.True(
            resolved is TileKind.Enemy or TileKind.Elite or TileKind.Merchant
                     or TileKind.Rest or TileKind.Treasure or TileKind.Event,
            $"Resolved kind {resolved} is not a valid Unknown target");
    }

    [Fact]
    public void Resolve_Unknown_AlreadyCached_ReusesValue()
    {
        var state = SampleStateAtUnknownNode(BaseCatalog) with
        {
            UnknownResolutions = ImmutableDictionary.CreateRange(
                new[] { new KeyValuePair<int, TileKind>(42, TileKind.Treasure) })
        };
        var s1 = NodeEffectResolver.Resolve(state, TileKind.Unknown, currentRow: 5, BaseCatalog, new SequentialRng(1UL));
        // Treasure 解決後は ActiveReward が立つ (Treasure tile の挙動)
        Assert.NotNull(s1.ActiveReward);
        Assert.Equal(TileKind.Treasure, s1.UnknownResolutions[42]);
    }

    [Fact]
    public void Resolve_Unknown_WithRelicWeightDelta_BiasesOutcome()
    {
        // 全 5+1 種 (Enemy/Elite/Merchant/Rest/Treasure/Event) を -100000 で 0 以下にし、
        // Merchant のみ +100 で唯一の正値にすることで決定的に Merchant が選択されるようにする。
        // (T8 review fix: dictionary 反復順序に依存しない決定論テストに強化)
        var fake = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(BaseCatalog,
            "merchant_magnet",
            new CardEffect[]
            {
                new CardEffect("unknownTileWeightDelta", EffectScope.Self, null, +100,    Name: "merchant", Trigger: "Passive"),
                new CardEffect("unknownTileWeightDelta", EffectScope.Self, null, -100000, Name: "enemy",    Trigger: "Passive"),
                new CardEffect("unknownTileWeightDelta", EffectScope.Self, null, -100000, Name: "elite",    Trigger: "Passive"),
                new CardEffect("unknownTileWeightDelta", EffectScope.Self, null, -100000, Name: "rest",     Trigger: "Passive"),
                new CardEffect("unknownTileWeightDelta", EffectScope.Self, null, -100000, Name: "treasure", Trigger: "Passive"),
                new CardEffect("unknownTileWeightDelta", EffectScope.Self, null, -100000, Name: "event",    Trigger: "Passive"),
            });
        var state = SampleStateAtUnknownNode(fake) with
        {
            Relics = new List<string> { "merchant_magnet" },
        };

        var s1 = NodeEffectResolver.Resolve(state, TileKind.Unknown, currentRow: 5, fake, new SequentialRng(1UL));

        Assert.Equal(TileKind.Merchant, s1.UnknownResolutions[42]);
    }

    [Fact]
    public void Resolve_Unknown_AllWeightsZeroByDelta_FallsBackToConfig()
    {
        // 全 6 種を -10000 で 0 以下にすると fallback で元 config を使う
        // (Event delta も含めて全 weight を真に 0 化することで fallback path を実際に exercise)
        var fake = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(BaseCatalog,
            "anti_everything",
            new CardEffect[]
            {
                new CardEffect("unknownTileWeightDelta", EffectScope.Self, null, -10000, Name: "enemy",    Trigger: "Passive"),
                new CardEffect("unknownTileWeightDelta", EffectScope.Self, null, -10000, Name: "elite",    Trigger: "Passive"),
                new CardEffect("unknownTileWeightDelta", EffectScope.Self, null, -10000, Name: "merchant", Trigger: "Passive"),
                new CardEffect("unknownTileWeightDelta", EffectScope.Self, null, -10000, Name: "rest",     Trigger: "Passive"),
                new CardEffect("unknownTileWeightDelta", EffectScope.Self, null, -10000, Name: "treasure", Trigger: "Passive"),
                new CardEffect("unknownTileWeightDelta", EffectScope.Self, null, -10000, Name: "event",    Trigger: "Passive"),
            });
        var state = SampleStateAtUnknownNode(fake) with
        {
            Relics = new List<string> { "anti_everything" },
        };

        // fallback により例外なく解決される
        var s1 = NodeEffectResolver.Resolve(state, TileKind.Unknown, currentRow: 5, fake, new SequentialRng(1UL));
        Assert.True(s1.UnknownResolutions.ContainsKey(42));
    }

    [Fact]
    public void Resolve_Unknown_NoRelics_BaseConfigUsed()
    {
        // relic 無しで解決すると base config の weight 分布に従う
        // (statistical assertion は test の安定性のため避け、解決結果が valid kind であることのみ確認)
        var state = SampleStateAtUnknownNode(BaseCatalog);
        var s1 = NodeEffectResolver.Resolve(state, TileKind.Unknown, currentRow: 5, BaseCatalog, new SequentialRng(42UL));
        var resolved = s1.UnknownResolutions[42];
        Assert.True(
            resolved is TileKind.Enemy or TileKind.Elite or TileKind.Merchant
                     or TileKind.Rest or TileKind.Treasure or TileKind.Event,
            $"Resolved kind {resolved} is not a valid Unknown target");
    }
}
