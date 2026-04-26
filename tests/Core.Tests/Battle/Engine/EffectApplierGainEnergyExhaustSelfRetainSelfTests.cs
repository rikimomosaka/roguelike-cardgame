using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class EffectApplierGainEnergyExhaustSelfRetainSelfTests
{
    private static IRng Rng() => new FakeRng(new int[10], new double[0]);

    private static BattleState MakeState(int energy = 1) =>
        new(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(BattleFixtures.Hero()),
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: energy, EnergyMax: 3,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0, LastPlayedOrigCost: null, NextCardComboFreePass: false,
            EncounterId: "enc_test");

    [Fact] public void GainEnergy_adds_to_energy()
    {
        var s = MakeState(energy: 1);
        var hero = s.Allies[0];
        var eff = new CardEffect("gainEnergy", EffectScope.Self, null, 2);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(3, next.Energy);
        Assert.Equal(BattleEventKind.GainEnergy, evs[0].Kind);
        Assert.Equal(2, evs[0].Amount);
    }

    [Fact] public void GainEnergy_can_exceed_max()
    {
        var s = MakeState(energy: 3);
        var hero = s.Allies[0];
        var eff = new CardEffect("gainEnergy", EffectScope.Self, null, 5);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(8, next.Energy);  // EnergyMax 超過 OK
    }

    [Fact] public void ExhaustSelf_emits_event_only()
    {
        var s = MakeState();
        var hero = s.Allies[0];
        var eff = new CardEffect("exhaustSelf", EffectScope.Self, null, 0);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(s, next);   // state 不変
        Assert.Single(evs);
        Assert.Equal(BattleEventKind.Exhaust, evs[0].Kind);
        Assert.Equal("self", evs[0].Note);
        Assert.Equal(1, evs[0].Amount);
    }

    [Fact] public void RetainSelf_is_no_op()
    {
        var s = MakeState();
        var hero = s.Allies[0];
        var eff = new CardEffect("retainSelf", EffectScope.Self, null, 0);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(s, next);
        Assert.Empty(evs);
    }
}
