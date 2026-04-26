using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class EffectApplierExhaustCardTests
{
    private static IRng Rng() => new FakeRng(new int[10], new double[0]);

    private static BattleState MakeState(
        ImmutableArray<BattleCardInstance> hand = default,
        ImmutableArray<BattleCardInstance> discard = default,
        ImmutableArray<BattleCardInstance> draw = default) =>
        new(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(BattleFixtures.Hero()),
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: 3, EnergyMax: 3,
            DrawPile: draw.IsDefault ? ImmutableArray<BattleCardInstance>.Empty : draw,
            Hand: hand.IsDefault ? ImmutableArray<BattleCardInstance>.Empty : hand,
            DiscardPile: discard.IsDefault ? ImmutableArray<BattleCardInstance>.Empty : discard,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0, LastPlayedOrigCost: null, NextCardComboFreePass: false,
            OwnedRelicIds: ImmutableArray<string>.Empty,
            Potions: ImmutableArray<string>.Empty,
            EncounterId: "enc_test");

    [Fact] public void Exhaust_from_hand_random_picks()
    {
        var hand = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "c1"),
            BattleFixtures.MakeBattleCard("defend", "c2"));
        var s = MakeState(hand: hand);
        var hero = s.Allies[0];
        var eff = new CardEffect("exhaustCard", EffectScope.Self, null, 1, Pile: "hand");
        var rng = new FakeRng(new[] { 0 }, new double[0]);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, rng, BattleFixtures.MinimalCatalog());
        Assert.Single(next.Hand);
        Assert.Equal("c2", next.Hand[0].InstanceId);
        Assert.Single(next.ExhaustPile);
        Assert.Equal("c1", next.ExhaustPile[0].InstanceId);
        Assert.Equal(BattleEventKind.Exhaust, evs[0].Kind);
        Assert.Equal("hand", evs[0].Note);
        Assert.Equal(1, evs[0].Amount);
    }

    [Fact] public void Exhaust_from_discard()
    {
        var discard = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "c1"));
        var s = MakeState(discard: discard);
        var hero = s.Allies[0];
        var eff = new CardEffect("exhaustCard", EffectScope.Self, null, 1, Pile: "discard");
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Empty(next.DiscardPile);
        Assert.Single(next.ExhaustPile);
    }

    [Fact] public void Exhaust_from_draw()
    {
        var draw = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "c1"));
        var s = MakeState(draw: draw);
        var hero = s.Allies[0];
        var eff = new CardEffect("exhaustCard", EffectScope.Self, null, 1, Pile: "draw");
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Empty(next.DrawPile);
        Assert.Single(next.ExhaustPile);
    }

    [Fact] public void Exhaust_clamps_to_pile_size()
    {
        var hand = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "c1"));
        var s = MakeState(hand: hand);
        var hero = s.Allies[0];
        var eff = new CardEffect("exhaustCard", EffectScope.Self, null, 5, Pile: "hand");
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Empty(next.Hand);
        Assert.Single(next.ExhaustPile);
        Assert.Equal(1, evs[0].Amount);
    }

    [Fact] public void Exhaust_empty_pile_emits_no_event()
    {
        var s = MakeState();
        var hero = s.Allies[0];
        var eff = new CardEffect("exhaustCard", EffectScope.Self, null, 2, Pile: "hand");
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Empty(evs);
    }

    [Fact] public void Exhaust_invalid_pile_throws()
    {
        var s = MakeState(hand: ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "c1")));
        var hero = s.Allies[0];
        var eff = new CardEffect("exhaustCard", EffectScope.Self, null, 1, Pile: "invalid");
        Assert.Throws<System.InvalidOperationException>(() =>
            EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog()));
    }

    [Fact] public void Exhaust_null_pile_throws()
    {
        var s = MakeState(hand: ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "c1")));
        var hero = s.Allies[0];
        var eff = new CardEffect("exhaustCard", EffectScope.Self, null, 1, Pile: null);
        Assert.Throws<System.InvalidOperationException>(() =>
            EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog()));
    }
}
