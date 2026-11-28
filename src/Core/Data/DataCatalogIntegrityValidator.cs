using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Events;

namespace RoguelikeCardGame.Core.Data;

/// <summary>
/// Cross-document validation for authoring/publishing. DataCatalog.LoadFromStrings remains
/// permissive for small test fixtures; the development workbench explicitly runs this stricter
/// graph validation before committing an override.
/// </summary>
public static class DataCatalogIntegrityValidator
{
    public static void Validate(DataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        CardPoolMetadataValidator.Validate(catalog.Cards, catalog.Characters);
        ValidateEventReferences(catalog.Events, catalog.Cards);

        foreach (var card in catalog.Cards.Values)
        {
            ValidateKeywords($"card '{card.Id}'", card.Keywords);
            ValidateKeywords($"card '{card.Id}' upgraded", card.UpgradedKeywords);
            ValidateEffects(catalog, $"card '{card.Id}'", card.Effects);
            if (card.UpgradedEffects is not null)
                ValidateEffects(catalog, $"card '{card.Id}' upgraded", card.UpgradedEffects);
            CardContentPolicyValidator.Validate(card, Fail);
        }
        foreach (var potion in catalog.Potions.Values)
            ValidateEffects(catalog, $"potion '{potion.Id}'", potion.Effects);
        foreach (var relic in catalog.Relics.Values)
            ValidateEffects(catalog, $"relic '{relic.Id}'", relic.Effects);
        foreach (var character in catalog.Characters.Values)
            foreach (var skill in character.EffectiveSkills)
            {
                ValidateEffects(catalog, $"character '{character.Id}' skill '{skill.Id}'", skill.Effects);
                if (skill.PhaseEffects is not null)
                    foreach (var (phase, phaseEffects) in skill.PhaseEffects)
                        ValidateEffects(catalog, $"character '{character.Id}' skill '{skill.Id}' phase '{phase}'", phaseEffects);
            }
        foreach (var enemy in catalog.Enemies.Values)
            ValidateEnemy(catalog, enemy);
        if (catalog.Units is not null)
            foreach (var unit in catalog.Units.Values)
                ValidateUnit(catalog, unit);
    }

    internal static void ValidateEventReferences(
        IReadOnlyDictionary<string, EventDefinition> events,
        IReadOnlyDictionary<string, CardDefinition> cards)
    {
        foreach (var eventDefinition in events.Values)
        {
            for (var choiceIndex = 0; choiceIndex < eventDefinition.Choices.Length; choiceIndex++)
            {
                var choice = eventDefinition.Choices[choiceIndex];
                for (var effectIndex = 0; effectIndex < choice.Effects.Length; effectIndex++)
                {
                    if (choice.Effects[effectIndex] is not EventEffect.GainCard gainCard)
                        continue;
                    var location =
                        $"event '{eventDefinition.Id}' choices[{choiceIndex}] effects[{effectIndex}]";
                    if (string.IsNullOrWhiteSpace(gainCard.CardId))
                        Fail(location, "gainCard requires a non-empty cardId.");
                    if (!cards.ContainsKey(gainCard.CardId))
                        Fail(location, $"gainCard references unknown card '{gainCard.CardId}'.");
                }
            }
        }
    }

    private static void ValidateEnemy(DataCatalog catalog, EnemyDefinition enemy)
    {
        var location = $"enemy '{enemy.Id}'";
        if (enemy.Attack < 0 || enemy.Defense < 0)
            Fail(location, "attack and defense must be non-negative.");
        foreach (var openingCardId in enemy.EffectiveOpeningActions)
        {
            // 先制行動はどの効果のカードでも使える（2026-11-28 から攻撃も可）。
            if (!catalog.Cards.ContainsKey(openingCardId))
                Fail(location, $"openingActions references unknown card '{openingCardId}'.");
        }
        if (!enemy.IsCardControlled)
        {
            if (enemy.Deck is not null)
                Fail(location, "deck must contain at least one entry.");
            if (!string.IsNullOrWhiteSpace(enemy.AiProfileId))
                Fail(location, "aiProfileId requires a card-controlled deck.");
            ValidateMoves(catalog, location, enemy.Moves);
            return;
        }

        if (enemy.Moves.Count > 0 || !string.IsNullOrEmpty(enemy.InitialMoveId))
            Fail(location, "deck and moves/initialMoveId cannot be configured together.");
        if (string.IsNullOrWhiteSpace(enemy.AiProfileId))
            Fail(location, "a card-controlled enemy requires aiProfileId.");
        if (!UnitAiProfileIds.IsKnown(enemy.AiProfileId))
            Fail(location, $"references unknown aiProfileId '{enemy.AiProfileId}'.");
        if (enemy.InitialEnergy < 0 || enemy.EnergyPerTurn < 0
            || enemy.DrawPerTurn < 0 || enemy.HandLimit < 1)
            Fail(location, "card resources are outside their valid ranges.");

        var entries = new HashSet<(string CardId, bool Upgraded)>();
        foreach (var entry in enemy.EffectiveDeck)
        {
            if (string.IsNullOrWhiteSpace(entry.CardId))
                Fail(location, "deck contains an empty cardId.");
            if (entry.Count < 1)
                Fail(location, $"deck card '{entry.CardId}' has a non-positive count.");
            if (!catalog.Cards.TryGetValue(entry.CardId, out var card))
                Fail(location, $"deck references unknown card '{entry.CardId}'.");
            if (entry.Upgraded && !card.IsUpgradable)
                Fail(location, $"deck marks non-upgradable card '{entry.CardId}' as upgraded.");
            if (!entries.Add((entry.CardId, entry.Upgraded)))
                Fail(location, $"deck contains duplicate card '{entry.CardId}' (upgraded={entry.Upgraded}).");
            // 敵の計画は人間の選択待ちを持てないため、選択効果を持つカードは現段階では使用不可。
            var effects = entry.Upgraded && card.UpgradedEffects is not null
                ? card.UpgradedEffects
                : card.Effects;
            foreach (var effect in effects)
            {
                if (effect.Select == "choose" || effect.Action == "summon")
                    Fail(location,
                        $"deck card '{entry.CardId}' uses '{effect.Action}' (select={effect.Select}), which enemy card play does not support yet.");
            }
        }
    }

    private static void ValidateUnit(DataCatalog catalog, UnitDefinition unit)
    {
        var location = $"unit '{unit.Id}'";
        if (!unit.IsCardControlled)
        {
            if (unit.Deck is not null)
                Fail(location, "deck must contain at least one entry.");
            if (!string.IsNullOrWhiteSpace(unit.AiProfileId))
                Fail(location, "aiProfileId requires a card-controlled deck.");
            if (unit.EffectiveSkills.Count > 0
                || unit.EffectiveOwnedRelicIds.Count > 0
                || unit.EffectivePotions.Count > 0)
                Fail(location, "skills, ownedRelicIds, and potions require a card-controlled deck.");
            ValidateMoves(catalog, location, unit.Moves);
            return;
        }

        if (unit.Moves.Count > 0 || !string.IsNullOrEmpty(unit.InitialMoveId))
            Fail(location, "deck and moves/initialMoveId cannot be configured together.");
        if (string.IsNullOrWhiteSpace(unit.AiProfileId))
            Fail(location, "a card-controlled unit requires aiProfileId.");
        if (!UnitAiProfileIds.IsKnown(unit.AiProfileId))
            Fail(location, $"references unknown aiProfileId '{unit.AiProfileId}'.");
        if (unit.InitialEnergy < 0 || unit.EnergyPerTurn < 0 || unit.DrawPerTurn < 0 || unit.HandLimit < 1)
            Fail(location, "card resources are outside their valid ranges.");

        var entries = new HashSet<(string CardId, bool Upgraded)>();
        foreach (var entry in unit.EffectiveDeck)
        {
            if (string.IsNullOrWhiteSpace(entry.CardId))
                Fail(location, "deck contains an empty cardId.");
            if (entry.Count < 1)
                Fail(location, $"deck card '{entry.CardId}' has a non-positive count.");
            if (!catalog.Cards.TryGetValue(entry.CardId, out var card))
                Fail(location, $"deck references unknown card '{entry.CardId}'.");
            if (entry.Upgraded && !card.IsUpgradable)
                Fail(location, $"deck marks non-upgradable card '{entry.CardId}' as upgraded.");
            if (!entries.Add((entry.CardId, entry.Upgraded)))
                Fail(location, $"deck contains duplicate card '{entry.CardId}' (upgraded={entry.Upgraded}).");
        }

        ValidateUnitSkills(catalog, location, unit.EffectiveSkills);

        var relicIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relicId in unit.EffectiveOwnedRelicIds)
        {
            if (string.IsNullOrWhiteSpace(relicId))
                Fail(location, "ownedRelicIds contains an empty relic ID.");
            if (!catalog.Relics.ContainsKey(relicId))
                Fail(location, $"ownedRelicIds references unknown relic '{relicId}'.");
            if (!relicIds.Add(relicId))
                Fail(location, $"ownedRelicIds contains duplicate relic '{relicId}'.");
        }

        foreach (var potionId in unit.EffectivePotions)
        {
            if (string.IsNullOrWhiteSpace(potionId))
                Fail(location, "potions contains an empty potion ID.");
            if (!catalog.Potions.ContainsKey(potionId))
                Fail(location, $"potions references unknown potion '{potionId}'.");
        }
    }

    private static void ValidateUnitSkills(
        DataCatalog catalog,
        string location,
        IReadOnlyList<CharacterSkillDefinition> skills)
    {
        var skillIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var skill in skills)
        {
            var skillLocation = $"{location} skill '{skill.Id}'";
            if (string.IsNullOrWhiteSpace(skill.Id))
                Fail(location, "skills contains an empty skill ID.");
            if (string.IsNullOrWhiteSpace(skill.Name))
                Fail(skillLocation, "name must not be empty.");
            if (!skillIds.Add(skill.Id))
                Fail(location, $"skill ID '{skill.Id}' is duplicated.");
            if (!skill.Cost.IsValid)
                Fail(skillLocation, "resource cost cannot be negative.");
            if (skill.UsesPerTurn < 0)
                Fail(skillLocation, "usesPerTurn cannot be negative.");
            if (skill.Effects.Count == 0
                && (skill.PhaseEffects is null || skill.PhaseEffects.Values.All(list => list.Count == 0)))
                Fail(skillLocation, "effects must not be empty.");

            ValidateKeywords(skillLocation, skill.Keywords);
            ValidateEffects(catalog, skillLocation, skill.Effects);
        }
    }

    private static void ValidateKeywords(string location, IReadOnlyList<string>? keywords)
    {
        if (keywords is null) return;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var keyword in keywords)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                Fail(location, "contains an empty keyword ID.");
            if (CardKeywords.Get(keyword) is null)
                Fail(location, $"references unknown keyword '{keyword}'.");
            if (!seen.Add(keyword))
                Fail(location, $"contains duplicate keyword '{keyword}'.");
        }
    }

    private static void ValidateMoves(DataCatalog catalog, string location, IReadOnlyList<MoveDefinition> moves)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var move in moves)
        {
            if (!ids.Add(move.Id)) Fail(location, $"move ID '{move.Id}' is duplicated.");
        }
        foreach (var move in moves)
        {
            if (!ids.Contains(move.NextMoveId))
                Fail(location, $"move '{move.Id}' references unknown nextMoveId '{move.NextMoveId}'.");
            ValidateEffects(catalog, $"{location} move '{move.Id}'", move.Effects);
        }
    }

    /// <summary>位相（setPhase）とカードの後付け能力（augmentCards）の項目チェック。</summary>
    internal static string? PhaseOrAugmentError(CardEffect effect)
    {
        if (effect.Action == "setPhase" && effect.Name is not ("none" or "calm" or "motion"))
            return $"setPhase requires phase name none|calm|motion, got '{effect.Name}'.";
        if (effect.Action != "augmentCards") return null;
        if (string.IsNullOrEmpty(effect.Name) || CardKeywords.Get(effect.Name) is null)
            return $"augmentCards requires a known keyword name, got '{effect.Name}'.";
        if (effect.Amount <= 0)
            return "augmentCards amount must be positive.";
        if (effect.Scope != EffectScope.Self)
            return "augmentCards requires scope self.";
        if ((effect.Pile ?? "hand") is not ("hand" or "draw" or "discard" or "exhaust" or "self"))
            return "augmentCards requires pile hand|draw|discard|exhaust|self.";
        if (effect.Pile != "self" && effect.Select is not ("all" or "random" or "choose"))
            return "augmentCards requires select all|random|choose.";
        if (!string.IsNullOrEmpty(effect.CardTypeFilter)
            && !System.Enum.TryParse<CardType>(effect.CardTypeFilter, ignoreCase: true, out _))
            return $"augmentCards cardTypeFilter '{effect.CardTypeFilter}' is not a card type.";
        return null;
    }

    private static void ValidatePhaseAndAugment(string location, CardEffect effect)
    {
        var error = PhaseOrAugmentError(effect);
        if (error is not null) Fail(location, error);
    }

    private static void ValidateEffects(DataCatalog catalog, string location, IReadOnlyList<CardEffect> effects)
    {
        for (var index = 0; index < effects.Count; index++)
        {
            var effect = effects[index];
            var effectLocation = $"{location} effects[{index}]";
            if (!string.IsNullOrWhiteSpace(effect.CardRefId) && !catalog.Cards.ContainsKey(effect.CardRefId))
                Fail(effectLocation, $"references unknown cardRefId '{effect.CardRefId}'.");
            if (!string.IsNullOrWhiteSpace(effect.UnitId) &&
                (catalog.Units is null || !catalog.Units.ContainsKey(effect.UnitId)))
                Fail(effectLocation, $"references unknown unitId '{effect.UnitId}'.");
            if (effect.AmountSource is not null &&
                !ContentAuthoringCatalog.AmountSources.Contains(effect.AmountSource, StringComparer.Ordinal))
            {
                Fail(effectLocation,
                    $"amountSource '{effect.AmountSource}' is not supported by the current Core.");
            }
            if (effect.AmountSource == "currentCost" && effect.Action != "modifyCardCost")
            {
                Fail(effectLocation,
                    "amountSource 'currentCost' is supported only by action 'modifyCardCost'.");
            }
            ValidatePhaseAndAugment(effectLocation, effect);
            if (effect.Action is "buff" or "debuff")
            {
                var changes = effect.EffectiveStatusChanges();
                if (changes.Count == 0)
                    Fail(effectLocation, $"action '{effect.Action}' requires a status name or statusChanges.");
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var change in changes)
                {
                    if (!seen.Add(change.Name))
                        Fail(effectLocation, $"contains duplicate status '{change.Name}'.");
                    StatusDefinition status;
                    try { status = StatusDefinition.Get(change.Name); }
                    catch (InvalidOperationException) { Fail(effectLocation, $"references unknown status '{change.Name}'."); return; }
                    var expected = effect.Action == "buff" ? StatusKind.Buff : StatusKind.Debuff;
                    // A negative signed-status adjustment intentionally uses the opposite verb in
                    // existing authoring data (for example: debuff strength by -2).
                    if (status.Kind != expected && change.Amount >= 0)
                        Fail(effectLocation, $"action '{effect.Action}' cannot apply {status.Kind} status '{status.Id}'.");
                    if (change.Name == BattleStatusIds.VirulentPoison && change.Amount > 0 && change.Amount != 1)
                        Fail(effectLocation, $"status '{BattleStatusIds.VirulentPoison}' must be applied one stack at a time.");
                }
            }
        }
    }

    [DoesNotReturn]
    private static void Fail(string location, string message)
        => throw new DataCatalogException($"{location}: {message}");
}
