using System;
using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Data;

/// <summary>Shared authoring safeguards for reusable reward-card content.</summary>
internal static class CardContentPolicyValidator
{
    internal static void Validate(CardDefinition card, Action<string, string> fail)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(fail);

        ValidateVersion(
            card,
            "normal",
            card.Effects,
            card.Keywords,
            card.Abilities,
            card.UsesPerTurn,
            fail);

        if (!card.IsUpgradable) return;
        ValidateVersion(
            card,
            "upgraded",
            card.UpgradedEffects ?? card.Effects,
            card.UpgradedKeywords,
            card.UpgradedAbilities,
            card.UpgradedUsesPerTurn ?? 0,
            fail);
    }

    private static void ValidateVersion(
        CardDefinition card,
        string version,
        IReadOnlyList<CardEffect> effects,
        IReadOnlyList<string>? keywords,
        IReadOnlyList<CardAbilityDefinition>? abilities,
        int usesPerTurn,
        Action<string, string> fail)
    {
        string location = $"card '{card.Id}' {version}";
        if (card.CardType is CardType.Attack or CardType.Skill &&
            effects.Any(effect => effect.Action is "draw" or "drawCards") &&
            usesPerTurn != 1)
        {
            fail(location,
                "Attack/Skill cards with an immediate draw effect require usesPerTurn = 1.");
        }

        bool grantsDurableDefense = effects
            .Where(effect => effect.Action == "buff")
            .SelectMany(effect => effect.EffectiveStatusChanges())
            .Any(change => change.Name is "barrier" or "revive" && change.Amount > 0);
        if (card.CardType is CardType.Attack or CardType.Skill &&
            grantsDurableDefense &&
            !(keywords?.Contains("exhaust", StringComparer.Ordinal) ?? false))
        {
            fail(location,
                "Attack/Skill cards that grant barrier or revive require keyword 'exhaust'.");
        }

        ValidateAbilityDefinitions(card.CardType, effects, abilities, location, fail);
    }

    private static void ValidateAbilityDefinitions(
        CardType cardType,
        IReadOnlyList<CardEffect> effects,
        IReadOnlyList<CardAbilityDefinition>? abilities,
        string location,
        Action<string, string> fail)
    {
        for (var index = 0; index < effects.Count; index++)
        {
            if (effects[index].MaxComboMin is <= 0)
                fail($"{location} effects[{index}]", "maxComboMin must be positive when specified.");
        }

        if (abilities is null) return;

        var seen = new HashSet<CardAbilityKind>();
        bool hasReprise = false;
        bool hasContingency = false;
        for (var index = 0; index < abilities.Count; index++)
        {
            var ability = abilities[index];
            var abilityLocation = $"{location} abilities[{index}]";
            if (ability is null)
            {
                fail(abilityLocation, "ability must not be null.");
                continue;
            }
            if (!seen.Add(ability.Kind))
                fail(abilityLocation, $"contains duplicate ability kind '{ability.Kind}'.");

            if (cardType is not (CardType.Attack or CardType.Skill))
            {
                fail(abilityLocation,
                    $"{ability.Kind} is currently supported only on Attack/Skill cards.");
            }

            switch (ability)
            {
                case FinisherAbilityDefinition:
                    break;
                case RepriseAbilityDefinition reprise:
                    hasReprise = true;
                    ValidateRepriseFilter(reprise.Filter, abilityLocation, fail);
                    break;
                case ContingencyAbilityDefinition contingency:
                    hasContingency = true;
                    ValidateContingencyTrigger(contingency.Trigger, abilityLocation, fail);
                    break;
                default:
                    fail(abilityLocation, $"unsupported typed ability '{ability.GetType().Name}'.");
                    break;
            }
        }

        if (hasReprise && hasContingency)
        {
            fail(location,
                "contingency and reprise cannot coexist on the same card version.");
        }
    }

    private static void ValidateRepriseFilter(
        RepriseCardFilter? filter,
        string location,
        Action<string, string> fail)
    {
        if (filter is null)
        {
            fail(location, "reprise requires a filter.");
            return;
        }

        ValidateRange(filter.MinOriginalCost, filter.MaxOriginalCost, "original cost", location, fail);
        ValidateRange(filter.MinPaidCost, filter.MaxPaidCost, "paid cost", location, fail);

        var cardTypes = new HashSet<CardType>();
        foreach (var cardType in filter.EffectiveCardTypes)
        {
            if (!cardTypes.Add(cardType))
                fail(location, $"reprise filter contains duplicate card type '{cardType}'.");
            if (cardType is not (CardType.Attack or CardType.Skill))
                fail(location, $"reprise filter card type '{cardType}' is not currently supported.");
        }

        var keywords = new HashSet<string>(StringComparer.Ordinal);
        foreach (var keyword in filter.EffectiveRequiredKeywords)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                fail(location, "reprise filter contains an empty required keyword.");
            if (!keywords.Add(keyword))
                fail(location, $"reprise filter contains duplicate keyword '{keyword}'.");
            if (CardKeywords.Get(keyword) is null)
                fail(location, $"reprise filter references unknown keyword '{keyword}'.");
            if (keyword is "momentum" or "finisher" or "reprise" or "contingency")
            {
                fail(location,
                    $"reprise filter keyword '{keyword}' is a typed ability, not an ordinary string keyword.");
            }
        }
    }

    private static void ValidateRange(
        int? min,
        int? max,
        string label,
        string location,
        Action<string, string> fail)
    {
        if (min < 0 || max < 0)
            fail(location, $"reprise {label} limits must be non-negative.");
        if (min is int lower && max is int upper && lower > upper)
            fail(location, $"reprise {label} minimum cannot exceed maximum.");
    }

    private static void ValidateContingencyTrigger(
        ContingencyTriggerDefinition? trigger,
        string location,
        Action<string, string> fail)
    {
        if (trigger is null)
        {
            fail(location, "contingency requires a trigger.");
            return;
        }
        if (!Enum.IsDefined(trigger.Timing))
            fail(location, $"unknown contingency trigger timing '{trigger.Timing}'.");
        if (trigger.Timing == ContingencyTriggerTiming.ComboReached)
        {
            if (trigger.ComboThreshold is null or <= 0)
                fail(location, "ComboReached contingency requires a positive combo threshold.");
        }
        else if (trigger.ComboThreshold is not null)
        {
            fail(location, "combo threshold is valid only for ComboReached contingency.");
        }
    }
}
