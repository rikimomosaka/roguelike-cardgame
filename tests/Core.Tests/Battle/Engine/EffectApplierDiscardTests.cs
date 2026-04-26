using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class EffectApplierDiscardTests
{
    private static IRng Rng() => new FakeRng(new int[10], new double[0]);

    private static BattleState MakeState(ImmutableArray<BattleCardInstance> hand) =>
        new(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(BattleFixtures.Hero()),
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: 3, EnergyMax: 3,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: hand,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0, LastPlayedOrigCost: null, NextCardComboFreePass: false,
            EncounterId: "enc_test");

    [Fact] public void Discard_random_picks_via_rng()
    {
        var hand = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "c1"),
            BattleFixtures.MakeBattleCard("defend", "c2"),
            BattleFixtures.MakeBattleCard("strike", "c3"));
        var s = MakeState(hand);
        var hero = s.Allies[0];
        var eff = new CardEffect("discard", EffectScope.Random, null, 1);
        var rng = new FakeRng(new[] { 1 }, new double[0]);  // pick c2
        var (next, evs) = EffectApplier.Apply(s, hero, eff, rng, BattleFixtures.MinimalCatalog());
        Assert.Equal(2, next.Hand.Length);
        Assert.Equal(1, next.DiscardPile.Length);
        Assert.Equal("c2", next.DiscardPile[0].InstanceId);
        Assert.Equal(BattleEventKind.Discard, evs[0].Kind);
        Assert.Equal(1, evs[0].Amount);
        Assert.Equal("random", evs[0].Note);
    }

    [Fact] public void Discard_all_empties_hand()
    {
        var hand = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "c1"),
            BattleFixtures.MakeBattleCard("defend", "c2"));
        var s = MakeState(hand);
        var hero = s.Allies[0];
        var eff = new CardEffect("discard", EffectScope.All, null, 0);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Empty(next.Hand);
        Assert.Equal(2, next.DiscardPile.Length);
        Assert.Equal(2, evs[0].Amount);
        Assert.Equal("all", evs[0].Note);
    }

    [Fact] public void Discard_random_with_short_hand_clamps()
    {
        var hand = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "c1"));
        var s = MakeState(hand);
        var hero = s.Allies[0];
        var eff = new CardEffect("discard", EffectScope.Random, null, 5);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Empty(next.Hand);
        Assert.Equal(1, next.DiscardPile.Length);
        Assert.Equal(1, evs[0].Amount);  // 実捨て数 = 1
    }

    [Fact] public void Discard_empty_hand_emits_no_event()
    {
        var s = MakeState(ImmutableArray<BattleCardInstance>.Empty);
        var hero = s.Allies[0];
        var eff = new CardEffect("discard", EffectScope.Random, null, 2);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Empty(evs);
    }

    [Fact] public void Discard_single_throws()
    {
        var hand = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "c1"));
        var s = MakeState(hand);
        var hero = s.Allies[0];
        var eff = new CardEffect("discard", EffectScope.Single, null, 1);
        Assert.Throws<System.InvalidOperationException>(() =>
            EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog()));
    }

    [Fact] public void Discard_self_throws()
    {
        var s = MakeState(ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "c1")));
        var hero = s.Allies[0];
        var eff = new CardEffect("discard", EffectScope.Self, null, 1);
        Assert.Throws<System.InvalidOperationException>(() =>
            EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog()));
    }
}
