using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class BattleEnginePlayCardComboTests
{
    private static IRng Rng() => new FakeRng(new int[20], new double[0]);

    private static CardDefinition CardWithCost(string id, int cost, string[]? keywords = null) =>
        new(id, id, null, CardRarity.Common, CardType.Attack,
            Cost: cost, UpgradedCost: null,
            Effects: new[] { new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 1) },
            UpgradedEffects: null, Keywords: keywords);

    private static BattleState Make(
        ImmutableArray<BattleCardInstance> hand,
        int? lastOrigCost = null,
        int combo = 0,
        bool freePass = false,
        int energy = 10) =>
        new(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(BattleFixtures.Hero()),
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: energy, EnergyMax: 10,
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

    [Fact] public void Example1_normal_staircase()
    {
        var def = CardWithCost("c2", 2);
        var card = new BattleCardInstance("inst1", "c2", false, null);
        var hand = ImmutableArray.Create(card);
        var s = Make(hand, lastOrigCost: 1, combo: 1, energy: 5);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(4, next.Energy);
        Assert.Equal(2, next.ComboCount);
        Assert.Equal(2, next.LastPlayedOrigCost);
        Assert.False(next.NextCardComboFreePass);
    }

    [Fact] public void Normal_no_match_resets_combo_to_one()
    {
        var def = CardWithCost("c5", 5);
        var card = new BattleCardInstance("inst1", "c5", false, null);
        var hand = ImmutableArray.Create(card);
        var s = Make(hand, lastOrigCost: 1, combo: 2);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(1, next.ComboCount);
        Assert.Equal(5, next.LastPlayedOrigCost);
        Assert.Equal(5, next.Energy);
    }

    [Fact] public void Example2_wild_no_match_continues_no_reduction()
    {
        var def = CardWithCost("wild5", 5, keywords: new[] { "wild" });
        var card = new BattleCardInstance("inst1", "wild5", false, null);
        var hand = ImmutableArray.Create(card);
        var s = Make(hand, lastOrigCost: 1, combo: 1);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(5, next.Energy);
        Assert.Equal(2, next.ComboCount);
        Assert.Equal(5, next.LastPlayedOrigCost);
        Assert.False(next.NextCardComboFreePass);
    }

    [Fact] public void Example3_wild_with_match_reduces_normally()
    {
        var def = CardWithCost("wild2", 2, keywords: new[] { "wild" });
        var card = new BattleCardInstance("inst1", "wild2", false, null);
        var hand = ImmutableArray.Create(card);
        var s = Make(hand, lastOrigCost: 1, combo: 1);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(9, next.Energy);
        Assert.Equal(2, next.ComboCount);
        Assert.Equal(2, next.LastPlayedOrigCost);
    }

    [Fact] public void Example4_superwild_sets_free_pass()
    {
        var def = CardWithCost("sw7", 7, keywords: new[] { "superwild" });
        var card = new BattleCardInstance("inst1", "sw7", false, null);
        var hand = ImmutableArray.Create(card);
        var s = Make(hand, lastOrigCost: 1, combo: 1);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(3, next.Energy);
        Assert.Equal(2, next.ComboCount);
        Assert.Equal(7, next.LastPlayedOrigCost);
        Assert.True(next.NextCardComboFreePass);
    }

    [Fact] public void Example4_cont_next_card_bypasses_via_free_pass()
    {
        var def = CardWithCost("c3", 3);
        var card = new BattleCardInstance("inst1", "c3", false, null);
        var hand = ImmutableArray.Create(card);
        var s = Make(hand, lastOrigCost: 7, combo: 2, freePass: true);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(7, next.Energy);
        Assert.Equal(3, next.ComboCount);
        Assert.Equal(3, next.LastPlayedOrigCost);
        Assert.False(next.NextCardComboFreePass);
    }

    [Fact] public void Wild_and_superwild_both_present_superwild_wins()
    {
        var def = CardWithCost("ws", 4, keywords: new[] { "wild", "superwild" });
        var card = new BattleCardInstance("inst1", "ws", false, null);
        var hand = ImmutableArray.Create(card);
        var s = Make(hand, lastOrigCost: 1, combo: 1);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(2, next.ComboCount);
        Assert.True(next.NextCardComboFreePass);
    }

    [Fact] public void Example5_wild_after_reset_starts_at_combo_one()
    {
        var def = CardWithCost("wild5", 5, keywords: new[] { "wild" });
        var card = new BattleCardInstance("inst1", "wild5", false, null);
        var hand = ImmutableArray.Create(card);
        var s = Make(hand, lastOrigCost: null, combo: 0);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(1, next.ComboCount);
        Assert.Equal(5, next.LastPlayedOrigCost);
        Assert.False(next.NextCardComboFreePass);
    }

    [Fact] public void Example6_superwild_then_zero_cost()
    {
        var def1 = CardWithCost("sw6", 6, keywords: new[] { "superwild" });
        var card1 = new BattleCardInstance("inst1", "sw6", false, null);
        var s1 = Make(ImmutableArray.Create(card1), lastOrigCost: 4, combo: 2);
        var cat1 = BattleFixtures.MinimalCatalog(cards: new[] { def1 });
        var (after1, _) = BattleEngine.PlayCard(s1, 0, 0, 0, Rng(), cat1);
        Assert.Equal(3, after1.ComboCount);
        Assert.Equal(6, after1.LastPlayedOrigCost);
        Assert.True(after1.NextCardComboFreePass);

        var def2 = CardWithCost("c0", 0);
        var card2 = new BattleCardInstance("inst2", "c0", false, null);
        var s2 = after1 with {
            Hand = ImmutableArray.Create(card2),
        };
        var cat2 = BattleFixtures.MinimalCatalog(cards: new[] { def1, def2 });
        var (after2, _) = BattleEngine.PlayCard(s2, 0, 0, 0, Rng(), cat2);
        Assert.Equal(4, after2.ComboCount);
        Assert.Equal(0, after2.LastPlayedOrigCost);
        Assert.False(after2.NextCardComboFreePass);
        Assert.Equal(4, after2.Energy);
    }

    [Fact] public void Empty_keywords_array_treated_as_no_wild()
    {
        var def = CardWithCost("c5", 5, keywords: new string[0]);
        var card = new BattleCardInstance("inst1", "c5", false, null);
        var hand = ImmutableArray.Create(card);
        var s = Make(hand, lastOrigCost: 1, combo: 1);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(1, next.ComboCount);
        Assert.Equal(5, next.LastPlayedOrigCost);
    }
}
