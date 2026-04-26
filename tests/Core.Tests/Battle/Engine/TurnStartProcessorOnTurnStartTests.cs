using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class TurnStartProcessorOnTurnStartTests
{
    private static FakeRng MakeRng() => new FakeRng(new int[20], System.Array.Empty<double>());

    [Fact]
    public void OnTurnStart_relic_fires_after_Draw_before_TurnStart_event()
    {
        var relic = BattleFixtures.Relic("r", RelicTrigger.OnTurnStart, true,
            new CardEffect("gainEnergy", EffectScope.Self, null, 1));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });
        var state = BattleFixtures.MinimalState(
            ownedRelicIds: ImmutableArray.Create("r"),
            energy: 0, energyMax: 3);

        var (after, events) = TurnStartProcessor.Process(state, MakeRng(), catalog);

        // Energy reset to EnergyMax (=3), then OnTurnStart relic adds 1 → final 4
        Assert.Equal(4, after.Energy);
        // events 順序: ... TurnStart event 最後
        var lastEv = events[^1];
        Assert.Equal(BattleEventKind.TurnStart, lastEv.Kind);
        // GainEnergy event は TurnStart event より前
        var gainIdx = events.ToList().FindIndex(e => e.Kind == BattleEventKind.GainEnergy);
        var tsIdx = events.ToList().FindIndex(e => e.Kind == BattleEventKind.TurnStart);
        Assert.True(gainIdx < tsIdx);
    }

    [Fact]
    public void OnTurnStart_with_no_relics_keeps_existing_behavior()
    {
        var catalog = BattleFixtures.MinimalCatalog();
        var state = BattleFixtures.MinimalState(energy: 0);

        var (after, events) = TurnStartProcessor.Process(state, MakeRng(), catalog);

        Assert.Equal(after.EnergyMax, after.Energy);
        Assert.Contains(events, e => e.Kind == BattleEventKind.TurnStart);
    }

    [Fact]
    public void OnTurnStart_attack_relic_adds_to_hero_AttackPool()
    {
        var relic = BattleFixtures.Relic("attack_r", RelicTrigger.OnTurnStart, true,
            new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 3));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });
        var state = BattleFixtures.MinimalState(
            ownedRelicIds: ImmutableArray.Create("attack_r"));

        var (after, _) = TurnStartProcessor.Process(state, MakeRng(), catalog);

        Assert.Equal(3, after.Allies[0].AttackAll.Sum);
    }

    [Fact]
    public void OnTurnStart_Implemented_false_skipped()
    {
        var unimpl = BattleFixtures.Relic("unimpl", RelicTrigger.OnTurnStart,
            implemented: false,
            new CardEffect("gainEnergy", EffectScope.Self, null, 5));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { unimpl });
        var state = BattleFixtures.MinimalState(
            ownedRelicIds: ImmutableArray.Create("unimpl"),
            energy: 0, energyMax: 3);

        var (after, _) = TurnStartProcessor.Process(state, MakeRng(), catalog);

        Assert.Equal(3, after.Energy); // Energy 5 加算なし
    }
}
