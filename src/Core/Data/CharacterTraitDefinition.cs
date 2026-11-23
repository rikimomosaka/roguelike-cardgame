using System.Collections.Generic;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Data;

/// <summary>Supported timings for a character's data-driven passive trait.</summary>
public enum CharacterTraitTrigger
{
    OnTurnStart,
    OnComboReached,
    OnContingencyTriggered,
    OnResonanceConsumed,
}

/// <summary>
/// One character-owned passive trait. Effects share the same data model as
/// cards and character skills so content does not require character-specific
/// engine branches.
/// </summary>
public sealed record CharacterTraitDefinition(
    string Id,
    string Name,
    CharacterTraitTrigger Trigger,
    IReadOnlyList<CardEffect> Effects,
    int Threshold = 0,
    int UsesPerTurn = 0,
    string? Description = null)
{
    /// <summary>Zero means that the trait has no per-turn activation cap.</summary>
    public bool HasPerTurnLimit => UsesPerTurn > 0;
}
