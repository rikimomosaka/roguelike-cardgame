using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class EffectApplierDrawTests
{
    private static IRng Rng() => new FakeRng(new int[20], new double[0]);

    private static BattleState MakeState(
        ImmutableArray<BattleCardInstance> draw,
        ImmutableArray<BattleCardInstance> hand,
        ImmutableArray<BattleCardInstance> discard) =>
        new(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(BattleFixtures.Hero()),
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: 3, EnergyMax: 3,
            DrawPile: draw, Hand: hand, DiscardPile: discard,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0, LastPlayedOrigCost: null, NextCardComboFreePass: false,
            EncounterId: "enc_test");

    [Fact] public void Draw_2_from_full_pile()
    {
        var draw = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "c1"),
            BattleFixtures.MakeBattleCard("defend", "c2"),
            BattleFixtures.MakeBattleCard("strike", "c3"));
        var s = MakeState(draw, ImmutableArray<BattleCardInstance>.Empty, ImmutableArray<BattleCardInstance>.Empty);
        var hero = s.Allies[0];
        var eff = new CardEffect("draw", EffectScope.Self, null, 2);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(2, next.Hand.Length);
        Assert.Equal(1, next.DrawPile.Length);
        Assert.Single(evs);
        Assert.Equal(BattleEventKind.Draw, evs[0].Kind);
        Assert.Equal(2, evs[0].Amount);
    }

    [Fact] public void Draw_with_empty_draw_pile_shuffles_discard()
    {
        var discard = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "c1"),
            BattleFixtures.MakeBattleCard("defend", "c2"));
        var s = MakeState(ImmutableArray<BattleCardInstance>.Empty,
            ImmutableArray<BattleCardInstance>.Empty, discard);
        var hero = s.Allies[0];
        var eff = new CardEffect("draw", EffectScope.Self, null, 2);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(2, next.Hand.Length);
        Assert.Empty(next.DiscardPile);
    }

    [Fact] public void Draw_caps_at_hand_max_10()
    {
        var hand = ImmutableArray.CreateRange(
            Enumerable.Range(0, 9)
                .Select(i => BattleFixtures.MakeBattleCard("strike", $"h{i}")));
        var draw = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "c1"),
            BattleFixtures.MakeBattleCard("defend", "c2"),
            BattleFixtures.MakeBattleCard("strike", "c3"));
        var s = MakeState(draw, hand, ImmutableArray<BattleCardInstance>.Empty);
        var hero = s.Allies[0];
        var eff = new CardEffect("draw", EffectScope.Self, null, 3);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(10, next.Hand.Length);
        Assert.Equal(1, evs[0].Amount);  // 実ドロー数 = 1
    }

    [Fact] public void Draw_with_empty_draw_and_discard_emits_no_event()
    {
        var s = MakeState(
            ImmutableArray<BattleCardInstance>.Empty,
            ImmutableArray<BattleCardInstance>.Empty,
            ImmutableArray<BattleCardInstance>.Empty);
        var hero = s.Allies[0];
        var eff = new CardEffect("draw", EffectScope.Self, null, 2);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Empty(next.Hand);
        Assert.Empty(evs);
    }
}
