using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using RoguelikeCardGame.Core.Tests;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class BattleEngineEnergyDrawSnapshotTests
{
    private static readonly DataCatalog BaseCatalog = EmbeddedDataLoader.LoadCatalog();

    private static RunState SampleRun(params string[] relicIds) =>
        RunState.NewSoloRun(
            BaseCatalog, 1UL, 0,
            ImmutableDictionary<int, TileKind>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty, ImmutableArray<string>.Empty,
            new System.DateTimeOffset(2026, 5, 4, 0, 0, 0, System.TimeSpan.Zero)
        ) with { Relics = relicIds };

    /// <summary>
    /// 既存 catalog から最初の encounter を取得 (テスト用)。
    /// </summary>
    private static string FirstEncounterId(DataCatalog catalog)
    {
        return catalog.Encounters.Keys.OrderBy(k => k).First();
    }

    [Fact]
    public void Start_NoRelics_BaseEnergyAndDrawValues()
    {
        var run = SampleRun();
        var (state, _) = BattleEngine.Start(run, FirstEncounterId(BaseCatalog), new SequentialRng(1UL), BaseCatalog);
        Assert.Equal(BattleEngine.InitialEnergy, state.EnergyMax);
        Assert.Equal(TurnStartProcessor.DrawPerTurn, state.DrawPerTurn);
    }

    [Fact]
    public void Start_WithEnergyPerTurnBonus_SnapsHigherEnergyMax()
    {
        var fake = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(BaseCatalog,
            "energy_charm",
            new[] { new CardEffect("energyPerTurnBonus", EffectScope.Self, null, 1, Trigger: "Passive") });
        var run = SampleRun("energy_charm");
        var (state, _) = BattleEngine.Start(run, FirstEncounterId(fake), new SequentialRng(1UL), fake);
        Assert.Equal(BattleEngine.InitialEnergy + 1, state.EnergyMax);
    }

    [Fact]
    public void Start_WithCardsDrawnPerTurnBonus_SnapsHigherDrawPerTurn()
    {
        var fake = RelicCatalogTestHelpers.BuildCatalogWithFakeRelic(BaseCatalog,
            "draw_charm",
            new[] { new CardEffect("cardsDrawnPerTurnBonus", EffectScope.Self, null, 2, Trigger: "Passive") });
        var run = SampleRun("draw_charm");
        var (state, _) = BattleEngine.Start(run, FirstEncounterId(fake), new SequentialRng(1UL), fake);
        Assert.Equal(TurnStartProcessor.DrawPerTurn + 2, state.DrawPerTurn);
    }
}
