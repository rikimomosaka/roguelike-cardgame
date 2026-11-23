using System;
using System.Collections.Generic;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Data;

public sealed class CardAbilityContentPolicyTests
{
    [Fact]
    public void Valid_attack_skill_ability_definitions_pass_policy()
    {
        var card = Card(
            CardType.Skill,
            new CardAbilityDefinition[]
            {
                new RepriseAbilityDefinition(new RepriseCardFilter(
                    CardTypes: new[] { CardType.Attack, CardType.Skill },
                    MaxOriginalCost: 3)),
                new FinisherAbilityDefinition(),
            },
            effects: new[]
            {
                new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 11, MaxComboMin: 3),
            });

        Assert.Empty(Validate(card));
    }

    [Fact]
    public void Contingency_and_reprise_cannot_coexist()
    {
        var card = Card(
            CardType.Skill,
            new CardAbilityDefinition[]
            {
                new ContingencyAbilityDefinition(
                    new ContingencyTriggerDefinition(ContingencyTriggerTiming.TurnStart)),
                new RepriseAbilityDefinition(new RepriseCardFilter()),
            });

        Assert.Contains(Validate(card), error =>
            error.Contains("cannot coexist", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(CardType.Power)]
    [InlineData(CardType.Unit)]
    [InlineData(CardType.Status)]
    [InlineData(CardType.Curse)]
    public void Typed_card_abilities_are_initially_limited_to_attack_and_skill(CardType cardType)
    {
        var card = Card(
            cardType,
            new CardAbilityDefinition[] { new FinisherAbilityDefinition() });

        Assert.Contains(Validate(card), error =>
            error.Contains("only on Attack/Skill", StringComparison.Ordinal));
    }

    [Fact]
    public void Reprise_explicit_card_type_filter_rejects_power_and_unit()
    {
        var card = Card(
            CardType.Skill,
            new CardAbilityDefinition[]
            {
                new RepriseAbilityDefinition(new RepriseCardFilter(
                    CardTypes: new[] { CardType.Power, CardType.Unit })),
            });

        var errors = Validate(card);

        Assert.Contains(errors, error => error.Contains("Power", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Unit", StringComparison.Ordinal));
    }

    [Fact]
    public void Reprise_keyword_filter_does_not_alias_typed_abilities()
    {
        var card = Card(
            CardType.Skill,
            new CardAbilityDefinition[]
            {
                new RepriseAbilityDefinition(new RepriseCardFilter(
                    RequiredKeywords: new[] { "finisher" })),
            });

        Assert.Contains(Validate(card), error =>
            error.Contains("typed ability", StringComparison.Ordinal));
    }

    [Fact]
    public void Programmatically_created_invalid_parameters_are_rejected()
    {
        var card = Card(
            CardType.Attack,
            new CardAbilityDefinition[]
            {
                new ContingencyAbilityDefinition(
                    new ContingencyTriggerDefinition(
                        ContingencyTriggerTiming.ComboReached,
                        ComboThreshold: 0)),
            },
            effects: new[]
            {
                new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5, MaxComboMin: 0),
            });

        var errors = Validate(card);

        Assert.Contains(errors, error => error.Contains("maxComboMin", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("positive combo threshold", StringComparison.Ordinal));
    }

    [Fact]
    public void Normal_and_upgraded_abilities_are_validated_independently()
    {
        var card = Card(
            CardType.Skill,
            new CardAbilityDefinition[] { new FinisherAbilityDefinition() }) with
        {
            UpgradedCost = 0,
            UpgradedAbilities = new CardAbilityDefinition[]
            {
                new ContingencyAbilityDefinition(
                    new ContingencyTriggerDefinition(ContingencyTriggerTiming.TurnStart)),
                new RepriseAbilityDefinition(new RepriseCardFilter()),
            },
        };

        var errors = Validate(card);

        Assert.Single(errors);
        Assert.Contains("upgraded", errors[0], StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> Validate(CardDefinition card)
    {
        var errors = new List<string>();
        CardContentPolicyValidator.Validate(
            card,
            (location, message) => errors.Add($"{location}: {message}"));
        return errors;
    }

    private static CardDefinition Card(
        CardType cardType,
        CardAbilityDefinition[] abilities,
        CardEffect[]? effects = null) =>
        new(
            Id: "policy_card",
            Name: "Policy card",
            DisplayName: null,
            Rarity: CardRarity.Rare,
            CardType: cardType,
            Cost: 1,
            UpgradedCost: null,
            Effects: effects ?? Array.Empty<CardEffect>(),
            UpgradedEffects: null,
            Keywords: null,
            Abilities: abilities);
}
