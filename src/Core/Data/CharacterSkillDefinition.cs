using System.Collections.Generic;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Data;

/// <summary>Character-specific tactical/ultimate action definition.</summary>
public sealed record CharacterSkillDefinition(
    string Id,
    string Name,
    ResourceCost Cost,
    bool OncePerTurn,
    IReadOnlyList<CardEffect> Effects,
    string? Description = null,
    bool RequiresCardsInHand = false,
    IReadOnlyList<string>? Keywords = null,
    /// <summary>Maximum uses during one turn. Zero means unlimited.</summary>
    int UsesPerTurn = 0)
{
    /// <summary>Backward-compatible interpretation of the old boolean field.</summary>
    public int EffectiveUsesPerTurn => UsesPerTurn > 0 ? UsesPerTurn : (OncePerTurn ? 1 : 0);
}
