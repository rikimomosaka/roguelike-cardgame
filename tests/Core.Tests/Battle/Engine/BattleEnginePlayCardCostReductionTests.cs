using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class BattleEnginePlayCardCostReductionTests
{
    private static IRng Rng() => new FakeRng(new int[20], new double[0]);

    private static BattleState Make(
        ImmutableArray<BattleCardInstance> hand,
        int energy = 3,
        int? lastOrigCost = null,
        int combo = 0,
        bool freePass = false) =>
        new(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(BattleFixtures.Hero()),
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: energy, EnergyMax: 3,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: hand,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: combo,
            LastPlayedOrigCost: lastOrigCost,
            NextCardComboFreePass: freePass,
            OwnedRelicIds: ImmutableArray<string>.Empty,
            Potions: ImmutableArray<string>.Empty,
            EncounterId: "enc_test");

    private static CardDefinition CardWithCost(string id, int cost) =>
        new(id, id, null, CardRarity.Common, CardType.Attack,
            Cost: cost, UpgradedCost: null,
            Effects: new[] { new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 1) },
            UpgradedEffects: null, Keywords: null);

    [Fact] public void Cost_override_is_ignored_for_combo_orig_cost()
    {
        var def = CardWithCost("c2", 2);
        var card = new BattleCardInstance("inst1", "c2", false, CostOverride: 0);
        var hand = ImmutableArray.Create(card);
        var s = Make(hand, energy: 0);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(2, next.LastPlayedOrigCost);
    }

    [Fact] public void Pay_cost_uses_cost_override()
    {
        var def = CardWithCost("c3", 3);
        var card = new BattleCardInstance("inst1", "c3", false, CostOverride: 1);
        var hand = ImmutableArray.Create(card);
        var s = Make(hand, energy: 3);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(2, next.Energy);
    }

    [Fact] public void Combo_reduction_lowers_pay_cost_by_one()
    {
        var def = CardWithCost("c2", 2);
        var card = new BattleCardInstance("inst1", "c2", false, CostOverride: null);
        var hand = ImmutableArray.Create(card);
        var s = Make(hand, energy: 3, lastOrigCost: 1, combo: 1);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(2, next.Energy);
    }

    [Fact] public void Combo_reduction_clamps_pay_cost_to_zero()
    {
        var def = CardWithCost("c1", 1);
        var card = new BattleCardInstance("inst1", "c1", false, CostOverride: null);
        var hand = ImmutableArray.Create(card);
        var s = Make(hand, energy: 0, lastOrigCost: 0, combo: 1);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(0, next.Energy);
    }

    [Fact] public void Cost_override_with_combo_reduction_combines()
    {
        var def = CardWithCost("c3", 3);
        var card = new BattleCardInstance("inst1", "c3", false, CostOverride: 2);
        var hand = ImmutableArray.Create(card);
        var s = Make(hand, energy: 3, lastOrigCost: 2, combo: 1);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(2, next.Energy);
    }

    [Fact] public void Throws_when_energy_below_pay_cost_after_reduction()
    {
        var def = CardWithCost("c5", 5);
        var card = new BattleCardInstance("inst1", "c5", false, CostOverride: null);
        var hand = ImmutableArray.Create(card);
        var s = Make(hand, energy: 3);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var ex = Assert.Throws<System.InvalidOperationException>(() =>
            BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat));
        Assert.Contains("insufficient energy", ex.Message);
    }
}
