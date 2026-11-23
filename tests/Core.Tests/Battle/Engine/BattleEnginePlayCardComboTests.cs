using System;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
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
        bool wildUsed = false,
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
            EncounterId: "enc_test",
            WildUsedInCurrentCombo: wildUsed);

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
        Assert.Equal(2, next.MaxComboThisTurn);
        Assert.Equal(2, next.LastPlayedOrigCost);
        Assert.False(next.NextCardComboFreePass);
    }

    [Fact]
    public void Max_combo_trait_fires_before_the_threshold_card_effects()
    {
        var finisher = new CardDefinition(
            "threshold-card", "threshold-card", null,
            CardRarity.Common, CardType.Skill,
            Cost: 3, UpgradedCost: null,
            Effects: new[]
            {
                new CardEffect(
                    "buff", EffectScope.Self, null, 1,
                    Name: "vigor"),
            },
            UpgradedEffects: null,
            Keywords: null);
        var trait = new CharacterTraitDefinition(
            "combo-trait", "combo-trait",
            CharacterTraitTrigger.OnComboReached,
            new[]
            {
                new CardEffect(
                    "buff", EffectScope.Self, null, 1,
                    Name: "afterimage"),
            },
            Threshold: 4,
            UsesPerTurn: 1);
        var character = new CharacterDefinition(
            "combo-character", "combo-character", 80, 0, 3,
            new[] { finisher.Id },
            Trait: trait);
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { finisher }) with
        {
            Characters = new System.Collections.Generic.Dictionary<string, CharacterDefinition>
            {
                [character.Id] = character,
            },
        };
        var state = Make(
            ImmutableArray.Create(
                new BattleCardInstance("threshold-instance", finisher.Id, false, null)),
            lastOrigCost: 2,
            combo: 3,
            energy: 5) with
        {
            CharacterId = character.Id,
            MaxComboThisTurn = 3,
        };

        var (next, events) = BattleEngine.PlayCard(
            state, 0, 0, 0, Rng(), catalog);

        Assert.Equal(4, next.MaxComboThisTurn);
        Assert.Equal(1, next.Allies[0].GetStatus("afterimage"));
        Assert.Equal(1, next.Allies[0].GetStatus("vigor"));
        int traitEventIndex = events.ToList().FindIndex(battleEvent =>
            battleEvent.Kind == RoguelikeCardGame.Core.Battle.Events.BattleEventKind.CharacterTraitTriggered);
        int cardEffectIndex = events.ToList().FindIndex(battleEvent =>
            battleEvent.Kind == RoguelikeCardGame.Core.Battle.Events.BattleEventKind.ApplyStatus
            && battleEvent.Note == "vigor");
        Assert.InRange(traitEventIndex, 0, int.MaxValue);
        Assert.True(cardEffectIndex > traitEventIndex);
    }

    [Fact]
    public void Card_usage_limit_is_shared_by_definition_and_resets_on_turn_end()
    {
        var def = new CardDefinition(
            "limited",
            "limited",
            null,
            CardRarity.Common,
            CardType.Skill,
            Cost: 0,
            UpgradedCost: null,
            Effects: new CardEffect[]
            {
                new("attack", EffectScope.Single, EffectSide.Enemy, 1),
                new("retainSelf", EffectScope.Self, null, 0),
            },
            UpgradedEffects: null,
            Keywords: null,
            UsesPerTurn: 1);
        var card = new BattleCardInstance("limited-1", def.Id, false, null);
        var secondCopy = new BattleCardInstance("limited-2", def.Id, false, null);
        var state = Make(ImmutableArray.Create(card, secondCopy), energy: 0);
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { def });

        var (afterFirst, _) = BattleEngine.PlayCard(state, 0, 0, 0, Rng(), catalog);
        Assert.Equal(1, afterFirst.CardUsesThisTurn[def.Id]);
        Assert.Throws<InvalidOperationException>(() =>
            BattleEngine.PlayCard(afterFirst, 0, 0, 0, Rng(), catalog));
        Assert.Throws<InvalidOperationException>(() =>
            BattleEngine.PlayCard(afterFirst, 1, 0, 0, Rng(), catalog));

        var reset = afterFirst with { CardUsesThisTurn = ImmutableDictionary<string, int>.Empty };
        var (afterReset, _) = BattleEngine.PlayCard(reset, 0, 0, 0, Rng(), catalog);
        Assert.Equal(1, afterReset.CardUsesThisTurn[def.Id]);
    }

    [Fact]
    public void Normal_and_upgraded_copies_share_the_same_definition_usage_count()
    {
        var def = new CardDefinition(
            "shared-limit", "shared-limit", null,
            CardRarity.Common, CardType.Skill,
            Cost: 0, UpgradedCost: 0,
            Effects: new[] { new CardEffect("retainSelf", EffectScope.Self, null, 0) },
            UpgradedEffects: null,
            Keywords: null,
            UsesPerTurn: 1,
            UpgradedUsesPerTurn: 1);
        var normal = new BattleCardInstance("shared-normal", def.Id, false, null);
        var upgraded = new BattleCardInstance("shared-upgraded", def.Id, true, null);
        var state = Make(ImmutableArray.Create(normal, upgraded), energy: 0);
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { def });

        var (afterNormal, _) = BattleEngine.PlayCard(
            state, 0, 0, 0, Rng(), catalog);

        Assert.Equal(1, afterNormal.CardUsesThisTurn[def.Id]);
        Assert.Throws<InvalidOperationException>(() =>
            BattleEngine.PlayCard(afterNormal, 1, 0, 0, Rng(), catalog));
    }

    [Fact]
    public void Temperance_and_extravagance_persist_across_turns_and_each_drop_after_a_play()
    {
        var def = CardWithCost("status-cost", 3);
        var card = new BattleCardInstance("status-cost-instance", def.Id, false, null);
        var hero = BattleFixtures.Hero() with
        {
            Statuses = ImmutableDictionary<string, int>.Empty
                .Add("handCostReduction", 3)
                .Add("handCostIncrease", 2),
        };
        var state = Make(ImmutableArray.Create(card), energy: 10) with
        {
            Allies = ImmutableArray.Create(hero),
        };
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { def });

        var (afterTurnEnd, _) = TurnEndProcessor.Process(state, Rng(), catalog);
        Assert.Equal(3, afterTurnEnd.Allies[0].GetStatus("handCostReduction"));
        Assert.Equal(2, afterTurnEnd.Allies[0].GetStatus("handCostIncrease"));

        var (afterPlay, events) = BattleEngine.PlayCard(
            state, 0, 0, 0, Rng(), catalog);

        Assert.Equal(2, Assert.Single(events, e => e.Kind == RoguelikeCardGame.Core.Battle.Events.BattleEventKind.PlayCard).Amount);
        Assert.Equal(2, afterPlay.Allies[0].GetStatus("handCostReduction"));
        Assert.Equal(1, afterPlay.Allies[0].GetStatus("handCostIncrease"));
    }

    [Fact]
    public void Upgraded_card_uses_its_own_usage_limit_instead_of_the_normal_limit()
    {
        var def = new CardDefinition(
            "limited", "limited", null, CardRarity.Common, CardType.Skill,
            Cost: 0,
            UpgradedCost: 0,
            Effects: new[] { new CardEffect("retainSelf", EffectScope.Self, null, 0) },
            UpgradedEffects: null,
            Keywords: null,
            UsesPerTurn: 1,
            UpgradedUsesPerTurn: 2);
        var card = new BattleCardInstance("limited-upgraded", def.Id, true, null);
        var state = Make(ImmutableArray.Create(card), energy: 0);
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { def });

        var (afterFirst, _) = BattleEngine.PlayCard(state, 0, 0, 0, Rng(), catalog);
        var (afterSecond, _) = BattleEngine.PlayCard(afterFirst, 0, 0, 0, Rng(), catalog);

        Assert.Equal(2, afterSecond.CardUsesThisTurn[def.Id]);
        Assert.Throws<InvalidOperationException>(() =>
            BattleEngine.PlayCard(afterSecond, 0, 0, 0, Rng(), catalog));
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
        Assert.Equal(2, next.MaxComboThisTurn);
        Assert.Equal(5, next.LastPlayedOrigCost);
        Assert.Equal(5, next.Energy);
    }

    [Fact]
    public void Combo_carryover_opening_continues_any_cost_without_cost_reduction()
    {
        var def = CardWithCost("carryover-c5", 5);
        var card = new BattleCardInstance("carryover-instance", def.Id, false, null);
        var state = Make(
                ImmutableArray.Create(card),
                lastOrigCost: null,
                combo: 3,
                energy: 10)
            with
            {
                MaxComboThisTurn = 3,
                ComboCarryoverPending = true,
            };
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { def });

        Assert.False(BattleCardCostCalculator.IsComboReductionEligible(
            state, card, def, state.Allies[0]));
        Assert.False(BattleCardCostCalculator.IsComboContinuationWithoutContinuity(
            state, card, def));
        var projectedCost = BattleCardCostCalculator.EvaluateForHand(
            state, card, def, state.Allies[0]);
        Assert.True(projectedCost.ComboContinues);
        Assert.False(projectedCost.ComboRingHighlighted);
        Assert.False(projectedCost.ComboReductionEligible);

        var (next, _) = BattleEngine.PlayCard(
            state, 0, 0, 0, Rng(), catalog);

        Assert.Equal(5, next.Energy);
        Assert.Equal(4, next.ComboCount);
        Assert.Equal(4, next.MaxComboThisTurn);
        Assert.Equal(5, next.LastPlayedOrigCost);
        Assert.False(next.ComboCarryoverPending);
    }

    [Fact]
    public void Combo_carryover_from_zero_accepts_any_opening_cost_without_reduction()
    {
        var def = CardWithCost("carryover-zero-c5", 5);
        var card = new BattleCardInstance("carryover-zero-instance", def.Id, false, null);
        var state = Make(
                ImmutableArray.Create(card),
                lastOrigCost: null,
                combo: 0,
                energy: 10)
            with
            {
                MaxComboThisTurn = 0,
                ComboCarryoverPending = true,
            };
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { def });

        var projectedCost = BattleCardCostCalculator.EvaluateForHand(
            state, card, def, state.Allies[0]);
        Assert.True(projectedCost.ComboContinues);
        Assert.False(projectedCost.ComboRingHighlighted);
        Assert.False(projectedCost.ComboReductionEligible);
        Assert.Equal(5, projectedCost.PayableCost);

        var (next, _) = BattleEngine.PlayCard(
            state, 0, 0, 0, Rng(), catalog);

        Assert.Equal(5, next.Energy);
        Assert.Equal(1, next.ComboCount);
        Assert.Equal(1, next.MaxComboThisTurn);
        Assert.Equal(5, next.LastPlayedOrigCost);
        Assert.False(next.ComboCarryoverPending);
    }

    [Fact]
    public void Continuing_combo_updates_turn_maximum()
    {
        var def = CardWithCost("c2", 2);
        var state = Make(
            ImmutableArray.Create(new BattleCardInstance("inst1", "c2", false, null)),
            lastOrigCost: 1,
            combo: 4);
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { def });

        var (next, _) = BattleEngine.PlayCard(state, 0, 0, 0, Rng(), catalog);

        Assert.Equal(5, next.ComboCount);
        Assert.Equal(5, next.MaxComboThisTurn);
    }

    [Fact]
    public void Breaking_combo_keeps_earlier_turn_maximum()
    {
        var def = CardWithCost("c5", 5);
        var state = Make(
                ImmutableArray.Create(new BattleCardInstance("inst1", "c5", false, null)),
                lastOrigCost: 1,
                combo: 4)
            with { MaxComboThisTurn = 7 };
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { def });

        var (next, _) = BattleEngine.PlayCard(state, 0, 0, 0, Rng(), catalog);

        Assert.Equal(1, next.ComboCount);
        Assert.Equal(7, next.MaxComboThisTurn);
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
        Assert.True(next.NextCardComboFreePass);
        Assert.True(next.WildUsedInCurrentCombo);
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
        Assert.True(next.NextCardComboFreePass);
        Assert.True(next.WildUsedInCurrentCombo);
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
        Assert.False(next.WildUsedInCurrentCombo);
    }

    [Fact] public void Example4_cont_next_card_bypasses_via_free_pass()
    {
        var def = CardWithCost("c3", 3);
        var card = new BattleCardInstance("inst1", "c3", false, null);
        var hand = ImmutableArray.Create(card);
        var s = Make(hand, lastOrigCost: 7, combo: 2, freePass: true);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(8, next.Energy);
        Assert.Equal(3, next.ComboCount);
        Assert.Equal(3, next.LastPlayedOrigCost);
        Assert.False(next.NextCardComboFreePass);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Wild_and_superwild_both_present_superwild_wins(bool wildUsed)
    {
        var def = CardWithCost("ws", 4, keywords: new[] { "wild", "superwild" });
        var card = new BattleCardInstance("inst1", "ws", false, null);
        var hand = ImmutableArray.Create(card);
        var s = Make(hand, lastOrigCost: 1, combo: 1, wildUsed: wildUsed);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(2, next.ComboCount);
        Assert.True(next.NextCardComboFreePass);
        Assert.Equal(wildUsed, next.WildUsedInCurrentCombo);
        Assert.Equal(6, next.Energy);
    }

    [Fact]
    public void Embedded_upgraded_wisp_summon_uses_its_superwild_keyword()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var hand = ImmutableArray.Create(
            new BattleCardInstance("wisp_card", "starter_summon_3", true, null));
        var state = Make(hand, lastOrigCost: 1, combo: 1);

        var (afterFirst, _) = BattleEngine.PlayCard(state, 0, 0, 0, Rng(), catalog);

        Assert.Equal(2, afterFirst.ComboCount);
        Assert.True(afterFirst.NextCardComboFreePass);
        Assert.False(afterFirst.WildUsedInCurrentCombo);
        Assert.Equal(7, afterFirst.Energy);
        Assert.Contains(afterFirst.SummonHeld, card => card.CardDefinitionId == "starter_summon_3");

        var (afterSecond, _) = BattleEngine.PlayCard(
            afterFirst with
            {
                Hand = ImmutableArray.Create(
                    new BattleCardInstance("wisp_card_2", "starter_summon_3", true, null)),
            },
            0, 0, 0, Rng(), catalog);

        Assert.Equal(3, afterSecond.ComboCount);
        Assert.True(afterSecond.NextCardComboFreePass);
        Assert.False(afterSecond.WildUsedInCurrentCombo);
        // 1 枚目の SuperWild が予約した保護により、2 枚目も 1 軽減。
        Assert.Equal(5, afterSecond.Energy);
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
        Assert.True(next.NextCardComboFreePass);
        Assert.True(next.WildUsedInCurrentCombo);
        Assert.Equal(5, next.Energy);
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

    [Fact]
    public void Wild_protects_its_next_card_then_disables_a_later_wild_in_the_same_combo()
    {
        var firstWild = CardWithCost("wild4", 4, keywords: new[] { "wild" });
        var normal = CardWithCost("normal1", 1);
        var laterWild = CardWithCost("wild3", 3, keywords: new[] { "wild" });
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { firstWild, normal, laterWild });

        var state = Make(
            ImmutableArray.Create(new BattleCardInstance("w1", "wild4", false, null)),
            lastOrigCost: 1,
            combo: 1);

        var (afterWild, _) = BattleEngine.PlayCard(state, 0, 0, 0, Rng(), catalog);
        Assert.Equal(2, afterWild.ComboCount);
        Assert.True(afterWild.NextCardComboFreePass);
        Assert.True(afterWild.WildUsedInCurrentCombo);

        var (afterProtected, _) = BattleEngine.PlayCard(
            afterWild with
            {
                Hand = ImmutableArray.Create(
                    new BattleCardInstance("n1", "normal1", false, null)),
            },
            0, 0, 0, Rng(), catalog);
        Assert.Equal(3, afterProtected.ComboCount);
        Assert.False(afterProtected.NextCardComboFreePass);
        Assert.True(afterProtected.WildUsedInCurrentCombo);
        Assert.Equal(6, afterProtected.Energy);

        var (afterDisabledWild, _) = BattleEngine.PlayCard(
            afterProtected with
            {
                Hand = ImmutableArray.Create(
                    new BattleCardInstance("w2", "wild3", false, null)),
            },
            0, 0, 0, Rng(), catalog);
        Assert.Equal(1, afterDisabledWild.ComboCount);
        Assert.False(afterDisabledWild.NextCardComboFreePass);
        Assert.False(afterDisabledWild.WildUsedInCurrentCombo);
    }

    [Fact]
    public void Wild_that_was_disabled_when_breaking_a_combo_can_activate_again_on_a_later_play()
    {
        var disabledWild = CardWithCost("wild4", 4, keywords: new[] { "wild" });
        var nextWild = CardWithCost("wild0", 0, keywords: new[] { "wild" });
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { disabledWild, nextWild });
        var state = Make(
            ImmutableArray.Create(new BattleCardInstance("w1", "wild4", false, null)),
            lastOrigCost: 1,
            combo: 3,
            wildUsed: true);

        var (afterBreak, _) = BattleEngine.PlayCard(state, 0, 0, 0, Rng(), catalog);
        Assert.Equal(1, afterBreak.ComboCount);
        Assert.False(afterBreak.NextCardComboFreePass);
        Assert.False(afterBreak.WildUsedInCurrentCombo);

        var (afterReactivation, _) = BattleEngine.PlayCard(
            afterBreak with
            {
                Hand = ImmutableArray.Create(
                    new BattleCardInstance("w2", "wild0", false, null)),
            },
            0, 0, 0, Rng(), catalog);
        Assert.Equal(2, afterReactivation.ComboCount);
        Assert.True(afterReactivation.NextCardComboFreePass);
        Assert.True(afterReactivation.WildUsedInCurrentCombo);
    }

    [Fact]
    public void Adjacent_consumed_wild_uses_incoming_protection_but_does_not_renew_it()
    {
        var firstWild = CardWithCost("wild4", 4, keywords: new[] { "wild" });
        var secondWild = CardWithCost("wild1", 1, keywords: new[] { "wild" });
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { firstWild, secondWild });
        var state = Make(
            ImmutableArray.Create(new BattleCardInstance("w1", "wild4", false, null)),
            lastOrigCost: 1,
            combo: 1);

        var (afterFirst, _) = BattleEngine.PlayCard(state, 0, 0, 0, Rng(), catalog);
        var (afterSecond, _) = BattleEngine.PlayCard(
            afterFirst with
            {
                Hand = ImmutableArray.Create(
                    new BattleCardInstance("w2", "wild1", false, null)),
            },
            0, 0, 0, Rng(), catalog);

        Assert.Equal(3, afterSecond.ComboCount);
        Assert.False(afterSecond.NextCardComboFreePass);
        Assert.True(afterSecond.WildUsedInCurrentCombo);
        // 直前の Wild の保護で 1 軽減されるが、この Wild の効果は無効なので保護は更新しない。
        Assert.Equal(6, afterSecond.Energy);
    }

    [Fact]
    public void Consumed_wild_can_continue_normally_and_reduce_cost_without_renewing_protection()
    {
        var def = CardWithCost("wild2", 2, keywords: new[] { "wild" });
        var state = Make(
            ImmutableArray.Create(new BattleCardInstance("w1", "wild2", false, null)),
            lastOrigCost: 1,
            combo: 3,
            wildUsed: true,
            energy: 5);
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { def });

        var (next, _) = BattleEngine.PlayCard(state, 0, 0, 0, Rng(), catalog);

        Assert.Equal(4, next.ComboCount);
        Assert.Equal(4, next.Energy);
        Assert.False(next.NextCardComboFreePass);
        Assert.True(next.WildUsedInCurrentCombo);
    }

    [Fact]
    public void Superwild_can_repeat_and_does_not_disable_a_later_wild()
    {
        var firstSuper = CardWithCost("super4", 4, keywords: new[] { "superwild" });
        var secondSuper = CardWithCost("super1", 1, keywords: new[] { "superwild" });
        var laterWild = CardWithCost("wild3", 3, keywords: new[] { "wild" });
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { firstSuper, secondSuper, laterWild });
        var state = Make(
            ImmutableArray.Create(new BattleCardInstance("s1", "super4", false, null)),
            lastOrigCost: 1,
            combo: 1);

        var (afterFirst, _) = BattleEngine.PlayCard(state, 0, 0, 0, Rng(), catalog);
        Assert.True(afterFirst.NextCardComboFreePass);
        Assert.False(afterFirst.WildUsedInCurrentCombo);

        var (afterSecond, _) = BattleEngine.PlayCard(
            afterFirst with
            {
                Hand = ImmutableArray.Create(
                    new BattleCardInstance("s2", "super1", false, null)),
            },
            0, 0, 0, Rng(), catalog);
        Assert.Equal(3, afterSecond.ComboCount);
        Assert.True(afterSecond.NextCardComboFreePass);
        Assert.False(afterSecond.WildUsedInCurrentCombo);
        Assert.Equal(6, afterSecond.Energy);

        var (afterWild, _) = BattleEngine.PlayCard(
            afterSecond with
            {
                Hand = ImmutableArray.Create(
                    new BattleCardInstance("w1", "wild3", false, null)),
            },
            0, 0, 0, Rng(), catalog);
        Assert.Equal(4, afterWild.ComboCount);
        Assert.True(afterWild.NextCardComboFreePass);
        Assert.True(afterWild.WildUsedInCurrentCombo);
        Assert.Equal(4, afterWild.Energy);
    }

    [Fact]
    public void Superwild_remains_effective_after_wild_was_used()
    {
        var def = CardWithCost("super4", 4, keywords: new[] { "superwild" });
        var state = Make(
            ImmutableArray.Create(new BattleCardInstance("s1", "super4", false, null)),
            lastOrigCost: 1,
            combo: 3,
            wildUsed: true);
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { def });

        var (next, _) = BattleEngine.PlayCard(state, 0, 0, 0, Rng(), catalog);

        Assert.Equal(4, next.ComboCount);
        Assert.True(next.NextCardComboFreePass);
        Assert.True(next.WildUsedInCurrentCombo);
    }

    [Fact]
    public void Protected_superwild_reduces_renews_protection_and_preserves_wild_used_state()
    {
        var def = CardWithCost("super4", 4, keywords: new[] { "superwild" });
        var state = Make(
            ImmutableArray.Create(new BattleCardInstance("s1", "super4", false, null)),
            lastOrigCost: 1,
            combo: 3,
            freePass: true,
            wildUsed: true,
            energy: 5);
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { def });

        var (next, events) = BattleEngine.PlayCard(state, 0, 0, 0, Rng(), catalog);

        Assert.Equal(4, next.ComboCount);
        Assert.Equal(2, next.Energy);
        Assert.Equal(3, events[0].Amount);
        Assert.True(next.NextCardComboFreePass);
        Assert.True(next.WildUsedInCurrentCombo);
    }

    [Fact]
    public void First_normal_card_starts_combo_without_reduction()
    {
        var def = CardWithCost("normal3", 3);
        var state = Make(
            ImmutableArray.Create(new BattleCardInstance("n1", "normal3", false, null)),
            energy: 5);
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { def });

        var (next, _) = BattleEngine.PlayCard(state, 0, 0, 0, Rng(), catalog);

        Assert.Equal(1, next.ComboCount);
        Assert.Equal(2, next.Energy);
    }

    [Theory]
    // 通常カード
    [InlineData("normal", false, false, false, false)]
    [InlineData("normal", true,  false, false, true)]
    [InlineData("normal", false, true,  false, true)]
    [InlineData("normal", true,  true,  false, true)]
    // 有効な Wild（自身の効果だけで継続する場合は軽減なし）
    [InlineData("wild", false, false, false, false)]
    [InlineData("wild", true,  false, false, true)]
    [InlineData("wild", false, true,  false, true)]
    [InlineData("wild", true,  true,  false, true)]
    // すでに Wild 使用済みで、キーワード効果が無効な後続 Wild
    [InlineData("wild", false, false, true,  false)]
    [InlineData("wild", true,  false, true,  true)]
    [InlineData("wild", false, true,  true,  true)]
    [InlineData("wild", true,  true,  true,  true)]
    // SuperWild
    [InlineData("superwild", false, false, false, false)]
    [InlineData("superwild", true,  false, false, true)]
    [InlineData("superwild", false, true,  false, true)]
    [InlineData("superwild", true,  true,  false, true)]
    public void Cost_reduction_truth_table_depends_only_on_normal_match_or_incoming_protection(
        string cardKind,
        bool matchesNormal,
        bool incomingProtection,
        bool wildUsed,
        bool expectedReduced)
    {
        string[]? keywords = cardKind == "normal" ? null : new[] { cardKind };
        var def = CardWithCost("card4", 4, keywords);
        var state = Make(
            ImmutableArray.Create(new BattleCardInstance("c1", "card4", false, null)),
            lastOrigCost: matchesNormal ? 3 : 1,
            combo: 2,
            freePass: incomingProtection,
            wildUsed: wildUsed,
            energy: 5);
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { def });

        var (next, events) = BattleEngine.PlayCard(state, 0, 0, 0, Rng(), catalog);

        int expectedPayCost = expectedReduced ? 3 : 4;
        Assert.Equal(5 - expectedPayCost, next.Energy);
        Assert.Equal(expectedPayCost, events[0].Amount);
    }

    [Fact]
    public void Active_wild_continues_combo_without_requiring_cost_reduction()
    {
        var def = CardWithCost("active-wild", 4, keywords: new[] { "wild" });
        var card = new BattleCardInstance("active-wild-instance", def.Id, false, null);
        var state = Make(
            ImmutableArray.Create(card),
            lastOrigCost: 1,
            combo: 2,
            wildUsed: false,
            energy: 5);

        var result = BattleCardCostCalculator.EvaluateForHand(
            state, card, def, BattleFixtures.Hero());

        Assert.False(result.ComboReductionEligible);
        Assert.True(result.ComboContinues);
    }

    [Theory]
    [InlineData("wild")]
    [InlineData("superwild")]
    public void Incoming_protection_reduces_wild_and_superwild_when_normal_cost_misses(string keyword)
    {
        var def = CardWithCost("protected-keyword", 4, keywords: new[] { keyword });
        var state = Make(
            ImmutableArray.Create(
                new BattleCardInstance("k1", "protected-keyword", false, null)),
            lastOrigCost: 1,
            combo: 2,
            freePass: true,
            energy: 5);
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { def });

        var (next, _) = BattleEngine.PlayCard(state, 0, 0, 0, Rng(), catalog);

        Assert.Equal(3, next.ComboCount);
        Assert.Equal(2, next.Energy);
        Assert.True(next.NextCardComboFreePass);
    }

    [Fact]
    public void Superwild_reduces_when_it_matches_normal_cost_condition()
    {
        var def = CardWithCost("super3", 3, keywords: new[] { "superwild" });
        var state = Make(
            ImmutableArray.Create(new BattleCardInstance("s1", "super3", false, null)),
            lastOrigCost: 2,
            combo: 2,
            energy: 4);
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { def });

        var (next, _) = BattleEngine.PlayCard(state, 0, 0, 0, Rng(), catalog);

        Assert.Equal(3, next.ComboCount);
        Assert.Equal(2, next.Energy);
        Assert.True(next.NextCardComboFreePass);
    }

    [Fact]
    public void Zeroed_cards_still_chain_by_original_cost_and_consume_their_modifiers()
    {
        var one = CardWithCost("original-one", 1);
        var two = CardWithCost("original-two", 2);
        var first = new BattleCardInstance("one", one.Id, false, null)
            .AddCostModifier(new CardCostModifier(
                -1, CardCostModifierDuration.UntilTurnEnd, "ultimate"));
        var second = new BattleCardInstance("two", two.Id, false, null)
            .AddCostModifier(new CardCostModifier(
                -2, CardCostModifierDuration.UntilTurnEnd, "ultimate"));
        var state = Make(ImmutableArray.Create(first, second), energy: 0);
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { one, two });

        var (afterOne, firstEvents) = BattleEngine.PlayCard(
            state, 0, 0, 0, Rng(), catalog);
        var (afterTwo, secondEvents) = BattleEngine.PlayCard(
            afterOne, 0, 0, 0, Rng(), catalog);

        Assert.Equal(0, firstEvents[0].Amount);
        Assert.Equal(0, secondEvents[0].Amount);
        Assert.Equal(2, afterTwo.ComboCount);
        Assert.Equal(2, afterTwo.LastPlayedOrigCost);
        Assert.Equal(0, afterTwo.Energy);
        Assert.All(afterTwo.DiscardPile,
            card => Assert.Empty(card.EffectiveCostModifiers));
    }

    [Fact]
    public void PlayCard_event_captures_the_authoritative_post_play_combo_count()
    {
        var one = CardWithCost("combo-one", 1);
        var two = CardWithCost("combo-two", 2);
        var state = Make(
            ImmutableArray.Create(
                new BattleCardInstance("one", one.Id, false, null),
                new BattleCardInstance("two", two.Id, false, null)),
            energy: 5);
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { one, two });

        var (afterFirst, firstEvents) = BattleEngine.PlayCard(
            state, 0, 0, 0, Rng(), catalog);
        var (_, secondEvents) = BattleEngine.PlayCard(
            afterFirst, 0, 0, 0, Rng(), catalog);

        Assert.Equal(1, firstEvents[0].ComboCountAfter);
        Assert.Equal(2, secondEvents[0].ComboCountAfter);
        Assert.All(firstEvents.Skip(1), battleEvent =>
            Assert.Null(battleEvent.ComboCountAfter));
    }

    [Fact]
    public void Broken_combo_reports_one_instead_of_the_previous_state_count()
    {
        var low = CardWithCost("combo-break", 1);
        var state = Make(
            ImmutableArray.Create(
                new BattleCardInstance("break", low.Id, false, null)),
            lastOrigCost: 3,
            combo: 9,
            energy: 5);
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { low });

        var (_, events) = BattleEngine.PlayCard(
            state, 0, 0, 0, Rng(), catalog);

        Assert.Equal(1, events[0].ComboCountAfter);
    }
}
