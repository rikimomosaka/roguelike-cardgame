using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public sealed class BattleCardCostCalculatorTests
{
    private static CardDefinition Definition(int? cost = 3, int? upgradedCost = 2) =>
        new("cost-card", "Cost Card", null, CardRarity.Common, CardType.Skill,
            cost, upgradedCost, [], null, null);

    [Fact]
    public void Original_cost_uses_definition_and_ignores_all_runtime_changes()
    {
        var card = new BattleCardInstance("i", "cost-card", true, CostOverride: 9)
            .AddCostModifier(new CardCostModifier(-5, CardCostModifierDuration.UntilPlayed));

        Assert.Equal(2, BattleCardCostCalculator.GetOriginalCost(card, Definition()));
    }

    [Fact]
    public void Additive_card_modifiers_stack_and_combo_is_applied_last()
    {
        var card = new BattleCardInstance("i", "cost-card", false, CostOverride: null)
            .AddCostModifier(new CardCostModifier(-1, CardCostModifierDuration.UntilPlayed))
            .AddCostModifier(new CardCostModifier(+2, CardCostModifierDuration.UntilTurnEnd));

        var result = BattleCardCostCalculator.Evaluate(
            card, Definition(), BattleFixtures.Hero(), isInHand: true,
            comboReductionEligible: true);

        Assert.Equal(3, result.OriginalCost);
        Assert.Equal(4, result.CostBeforeCombo);
        Assert.Equal(3, result.PayableCost);
        Assert.True(result.ComboReductionEligible);
    }

    [Fact]
    public void Hero_cost_statuses_apply_only_while_card_is_in_hand()
    {
        var hero = BattleFixtures.Hero() with
        {
            Statuses = ImmutableDictionary<string, int>.Empty
                .Add(BattleCardCostCalculator.HandCostIncreaseStatusId, 2)
                .Add(BattleCardCostCalculator.HandCostReductionStatusId, 1),
        };
        var card = new BattleCardInstance("i", "cost-card", false, null);

        Assert.Equal(4, BattleCardCostCalculator.GetCostBeforeCombo(
            card, Definition(), hero, isInHand: true));
        Assert.Equal(3, BattleCardCostCalculator.GetCostBeforeCombo(
            card, Definition(), hero, isInHand: false));
    }

    [Fact]
    public void Costs_are_clamped_and_unplayable_definition_stays_null()
    {
        var reduced = new BattleCardInstance("i", "cost-card", false, null)
            .AddCostModifier(new CardCostModifier(int.MinValue, CardCostModifierDuration.UntilPlayed));
        Assert.Equal(0, BattleCardCostCalculator.GetCostBeforeCombo(
            reduced, Definition(), BattleFixtures.Hero(), isInHand: true));

        Assert.Null(BattleCardCostCalculator.GetCostBeforeCombo(
            reduced, Definition(cost: null, upgradedCost: null),
            BattleFixtures.Hero(), isInHand: true));
    }

    [Fact]
    public void Combo_reduction_is_applied_before_the_final_int_clamp()
    {
        var definition = Definition(cost: int.MaxValue, upgradedCost: null);
        var card = new BattleCardInstance("i", definition.Id, false, null)
            .AddCostModifier(new CardCostModifier(
                +1, CardCostModifierDuration.UntilPlayed));

        var result = BattleCardCostCalculator.Evaluate(
            card, definition, BattleFixtures.Hero(), isInHand: true,
            comboReductionEligible: true);

        Assert.Equal((long)int.MaxValue + 1,
            BattleCardCostCalculator.GetRawCostBeforeCombo(
                card, definition, BattleFixtures.Hero(), isInHand: true));
        Assert.Equal(int.MaxValue, result.CostBeforeCombo);
        Assert.Equal(int.MaxValue, result.PayableCost);
    }

    [Fact]
    public void Play_consumes_all_changes_but_turn_end_removes_only_temporary_modifiers()
    {
        var persistent = new CardCostModifier(-1, CardCostModifierDuration.UntilPlayed);
        var temporary = new CardCostModifier(+2, CardCostModifierDuration.UntilTurnEnd);
        var card = new BattleCardInstance("i", "cost-card", false, CostOverride: 4,
            ImmutableArray.Create(persistent, temporary));

        var afterTurn = card.RemoveCostModifiers(CardCostModifierDuration.UntilTurnEnd);
        Assert.Equal(4, afterTurn.CostOverride);
        Assert.True(afterTurn.EffectiveCostModifiers.SequenceEqual(
            ImmutableArray.Create(persistent)));

        var afterPlay = card.ConsumeCostChangesOnPlay();
        Assert.Null(afterPlay.CostOverride);
        Assert.Empty(afterPlay.EffectiveCostModifiers);
    }
}
