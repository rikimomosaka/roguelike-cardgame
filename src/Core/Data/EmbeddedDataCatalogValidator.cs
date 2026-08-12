using System;
using System.Collections.Generic;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Relics;

namespace RoguelikeCardGame.Core.Data;

/// <summary>
/// Validates the embedded production master data. This deliberately does not run
/// from <see cref="DataCatalog.LoadFromStrings"/>, because tests and development
/// overrides are allowed to construct intentionally incomplete definitions.
/// </summary>
internal static class EmbeddedDataCatalogValidator
{
    private static readonly HashSet<string> BattleTriggers = new(StringComparer.Ordinal)
    {
        "OnBattleStart",
        "OnTurnStart",
        "OnTurnEnd",
        "OnPlayCard",
        "OnCardDiscarded",
        "OnCardExhausted",
        "OnEnemyDeath",
        "OnHit",
    };

    private static readonly HashSet<string> NonBattleTriggers = new(StringComparer.Ordinal)
    {
        "OnPickup",
        "OnMapTileResolved",
        "OnEnterShop",
        "OnEnterRestSite",
        "OnRest",
        "OnRewardGenerated",
        "OnCardAddedToDeck",
    };

    // These actions are dispatched by EffectApplier when a battle relic trigger fires.
    // retainSelf/exhaustSelf are intentionally absent: they describe the card being
    // played and cannot have a useful producer when the effect belongs to a relic.
    private static readonly HashSet<string> BattleActions = new(StringComparer.Ordinal)
    {
        "attack",
        "block",
        "buff",
        "debuff",
        "heal",
        "draw",
        "drawCards",
        "discard",
        "gainEnergy",
        "exhaustCard",
        "upgrade",
        "summon",
        "selfDamage",
        "addCard",
        "recoverFromDiscard",
        "gainMaxEnergy",
        "modifyCardCost",
    };

    // NonBattleRelicEffects handles exactly these actions.
    private static readonly HashSet<string> NonBattleActions = new(StringComparer.Ordinal)
    {
        "gainMaxHp",
        "gainGold",
        "healHp",
    };

    // PassiveModifiers handles exactly these actions.
    private static readonly HashSet<string> PassiveActions = new(StringComparer.Ordinal)
    {
        "energyPerTurnBonus",
        "cardsDrawnPerTurnBonus",
        "rewardCardChoicesBonus",
        "restHealBonus",
        "goldRewardMultiplier",
        "shopPriceMultiplier",
        "rewardRerollAvailable",
        "unknownTileWeightDelta",
    };

    private static readonly HashSet<string> AmountSources = new(StringComparer.Ordinal)
    {
        "handCount",
        "drawPileCount",
        "discardPileCount",
        "exhaustPileCount",
        "selfHp",
        "selfHpLost",
        "selfBlock",
        "comboCount",
        "energy",
        "powerCardCount",
    };

    internal static void ValidateRelics(IEnumerable<string> relicJsonEntries)
    {
        ArgumentNullException.ThrowIfNull(relicJsonEntries);

        foreach (var json in relicJsonEntries)
        {
            RelicDefinition relic;
            try
            {
                relic = RelicJsonLoader.Parse(json);
            }
            catch (Exception ex) when (ex is RelicJsonException or FormatException or OverflowException)
            {
                throw new DataCatalogException($"Embedded relic JSON is invalid: {ex.Message}");
            }

            if (!relic.Implemented)
                continue;

            if (relic.Effects.Count == 0)
            {
                throw new DataCatalogException(
                    $"Embedded relic '{relic.Id}' is marked implemented but has no effects.");
            }

            for (var effectIndex = 0; effectIndex < relic.Effects.Count; effectIndex++)
                ValidateRelicEffect(relic.Id, effectIndex, relic.Effects[effectIndex]);
        }
    }

    private static void ValidateRelicEffect(string relicId, int effectIndex, CardEffect effect)
    {
        var location = $"Embedded relic '{relicId}' effects[{effectIndex}]";
        if (string.IsNullOrWhiteSpace(effect.Action))
            Fail(location, "action is required.");
        if (string.IsNullOrWhiteSpace(effect.Trigger))
            Fail(location, "trigger is required for every implemented relic effect.");

        ValidateActionForTrigger(location, effect.Action, effect.Trigger!);

        if (effect.ChancePercent is < 0 or > 100)
            Fail(location, $"chancePercent must be between 0 and 100, got {effect.ChancePercent}.");
        if (effect.ChancePercent is not null && effect.Trigger != "OnHit")
            Fail(location, "chancePercent is currently supported only by trigger 'OnHit'.");

        if (effect.AmountSource is not null && !AmountSources.Contains(effect.AmountSource))
            Fail(location, $"amountSource '{effect.AmountSource}' is not supported by the current Core.");

        if (effect.Trigger == "Passive")
        {
            RequireSelfOnly(location, effect);
            if (effect.Action == "unknownTileWeightDelta" &&
                effect.Name is not ("enemy" or "elite" or "merchant" or "rest" or "treasure" or "event"))
            {
                Fail(location,
                    "unknownTileWeightDelta requires name enemy|elite|merchant|rest|treasure|event.");
            }
            return;
        }

        if (NonBattleTriggers.Contains(effect.Trigger!))
        {
            RequireSelfOnly(location, effect);
            return;
        }

        ValidateBattleFields(location, effect);
    }

    private static void ValidateActionForTrigger(string location, string action, string trigger)
    {
        bool valid = trigger switch
        {
            "Passive" => PassiveActions.Contains(action),
            _ when BattleTriggers.Contains(trigger) => BattleActions.Contains(action),
            _ when NonBattleTriggers.Contains(trigger) => NonBattleActions.Contains(action),
            _ => false,
        };

        if (valid)
            return;

        if (trigger != "Passive" &&
            !BattleTriggers.Contains(trigger) &&
            !NonBattleTriggers.Contains(trigger))
        {
            Fail(location, $"trigger '{trigger}' is not supported by the current Core.");
        }

        Fail(location, $"action '{action}' is not supported for trigger '{trigger}' by the current Core.");
    }

    private static void ValidateBattleFields(string location, CardEffect effect)
    {
        switch (effect.Action)
        {
            case "attack":
                if (effect.Scope == EffectScope.Self)
                    Fail(location, "attack requires scope single|random|all.");
                if (effect.Side != EffectSide.Enemy)
                    Fail(location, "attack requires side 'enemy'.");
                break;

            case "block":
            case "buff":
            case "debuff":
                RequireSideForNonSelf(location, effect);
                break;

            case "heal":
                if (effect.Scope == EffectScope.Self)
                {
                    if (effect.Side is not null)
                        Fail(location, "scope 'self' must not specify side.");
                }
                else if (effect.Side != EffectSide.Ally)
                {
                    Fail(location, "non-self heal requires side 'ally'.");
                }
                break;

            case "discard":
                if (effect.Scope is not (EffectScope.Random or EffectScope.All))
                    Fail(location, "discard requires scope random|all.");
                RequireNoSide(location, effect);
                break;

            case "draw":
            case "drawCards":
            case "gainEnergy":
            case "gainMaxEnergy":
            case "selfDamage":
            case "summon":
            case "addCard":
            case "exhaustCard":
            case "upgrade":
            case "recoverFromDiscard":
            case "modifyCardCost":
                RequireSelfOnly(location, effect);
                break;
        }

        if (effect.Action is "buff" or "debuff")
            ValidateStatus(location, effect);

        if (effect.Action == "summon" && string.IsNullOrWhiteSpace(effect.UnitId))
            Fail(location, "summon requires unitId.");

        if (effect.Action == "addCard")
        {
            if (string.IsNullOrWhiteSpace(effect.CardRefId))
                Fail(location, "addCard requires cardRefId.");
            if (effect.Pile is not ("hand" or "draw" or "discard" or "exhaust"))
                Fail(location, "addCard requires pile hand|draw|discard|exhaust.");
        }

        if (effect.Action == "upgrade" &&
            effect.Pile is not ("hand" or "draw" or "discard"))
        {
            Fail(location, "upgrade requires pile hand|draw|discard.");
        }

        if (effect.Action == "recoverFromDiscard" && effect.Pile is not ("hand" or "exhaust"))
            Fail(location, "recoverFromDiscard requires pile hand|exhaust.");

        if (effect.Select is not null)
        {
            if (effect.Action is not ("discard" or "exhaustCard" or "upgrade" or "recoverFromDiscard" or "modifyCardCost"))
                Fail(location, $"action '{effect.Action}' does not support select.");
            if (effect.Select is not ("all" or "random" or "choose"))
                Fail(location, "select must be all|random|choose.");
        }

        if (effect.Action == "modifyCardCost")
        {
            if (effect.Amount == 0)
                Fail(location, "modifyCardCost amount must not be 0.");
            if (effect.Pile is not ("hand" or "draw" or "discard" or "exhaust"))
                Fail(location, "modifyCardCost requires pile hand|draw|discard|exhaust.");
            if (effect.Select is not ("all" or "random" or "choose"))
                Fail(location, "modifyCardCost requires select all|random|choose.");
            if (effect.Select == "choose" && !string.IsNullOrEmpty(effect.Trigger))
                Fail(location, "triggered modifyCardCost cannot use select choose.");
            if (effect.CostDuration is not ("untilPlayed" or "untilTurnEnd"))
                Fail(location, "modifyCardCost requires costDuration untilPlayed|untilTurnEnd.");
            if (effect.Select != "all" && effect.CardCount is <= 0)
                Fail(location, "modifyCardCost cardCount must be > 0 when specified.");
        }
    }

    private static void ValidateStatus(string location, CardEffect effect)
    {
        if (string.IsNullOrWhiteSpace(effect.Name))
            Fail(location, $"{effect.Action} requires name (status ID).");

        StatusDefinition status;
        try
        {
            status = StatusDefinition.Get(effect.Name!);
        }
        catch (InvalidOperationException)
        {
            Fail(location, $"status ID '{effect.Name}' does not exist.");
            return;
        }

        var expectedKind = effect.Action == "buff" ? StatusKind.Buff : StatusKind.Debuff;
        if (status.Kind != expectedKind)
        {
            Fail(location,
                $"action '{effect.Action}' cannot apply {status.Kind.ToString().ToLowerInvariant()} status '{status.Id}'.");
        }
    }

    private static void RequireSideForNonSelf(string location, CardEffect effect)
    {
        if (effect.Scope == EffectScope.Self)
        {
            if (effect.Side is not null)
                Fail(location, "scope 'self' must not specify side.");
            return;
        }

        if (effect.Side is null)
            Fail(location, $"action '{effect.Action}' with non-self scope requires side.");
    }

    private static void RequireSelfOnly(string location, CardEffect effect)
    {
        if (effect.Scope != EffectScope.Self)
            Fail(location, $"action '{effect.Action}' requires scope 'self'.");
        RequireNoSide(location, effect);
    }

    private static void RequireNoSide(string location, CardEffect effect)
    {
        if (effect.Side is not null)
            Fail(location, $"action '{effect.Action}' must not specify side.");
    }

    private static void Fail(string location, string message)
        => throw new DataCatalogException($"{location}: {message}");
}
