using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Data;

/// <summary>通常のカード取得プールに関する、文書をまたぐ不変条件。</summary>
public static class CardPoolMetadataValidator
{
    public static void Validate(
        IReadOnlyDictionary<string, CardDefinition> cards,
        IReadOnlyDictionary<string, CharacterDefinition> characters)
    {
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(characters);

        foreach (var card in cards.Values)
            ValidateCard(card, characters);
    }

    private static void ValidateCard(
        CardDefinition card,
        IReadOnlyDictionary<string, CharacterDefinition> characters)
    {
        var kinds = card.EffectivePoolKinds;
        var eligibleIds = card.EffectiveEligibleCharacterIds;
        var location = $"card '{card.Id}'";

        if (kinds.Count == 0)
        {
            if (card.PoolClass is not null)
                Fail(location, "poolClass cannot be set when poolKinds is empty.");
            if (eligibleIds.Count > 0)
                Fail(location, "eligibleCharacterIds cannot be set when poolKinds is empty.");
            return;
        }

        if (card.PoolClass is null)
            Fail(location, "poolClass is required when poolKinds is not empty.");

        var seenKinds = new HashSet<CardPoolKind>();
        foreach (var kind in kinds)
            if (!seenKinds.Add(kind))
                Fail(location, $"poolKinds contains duplicate value '{kind}'.");

        if (card.PoolClass == CardPoolClass.Universal && eligibleIds.Count > 0)
            Fail(location, "Universal cards cannot restrict eligibleCharacterIds.");

        var seenCharacters = new HashSet<string>(StringComparer.Ordinal);
        foreach (var characterId in eligibleIds)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                Fail(location, "eligibleCharacterIds contains an empty ID.");
            if (!seenCharacters.Add(characterId))
                Fail(location, $"eligibleCharacterIds contains duplicate ID '{characterId}'.");
            if (!characters.TryGetValue(characterId, out var character))
                Fail(location, $"eligibleCharacterIds references unknown character '{characterId}'.");
            if (card.PoolClass != CardPoolRules.ToCardPoolClass(character.CharacterClass))
            {
                Fail(location,
                    $"eligible character '{characterId}' belongs to {character.CharacterClass}, " +
                    $"not pool {card.PoolClass}.");
            }
        }
    }

    [DoesNotReturn]
    private static void Fail(string location, string message)
        => throw new DataCatalogException($"{location}: {message}");
}
