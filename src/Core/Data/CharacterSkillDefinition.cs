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
    int UsesPerTurn = 0,
    /// <summary>使用者が位相（静・動）を持つ時だけ使える（位相なしでは使えない）。</summary>
    bool RequiresPhase = false,
    /// <summary>
    /// 使用時の使用者の位相（none / calm / motion）ごとの効果。共通の Effects の後に実行する。
    /// 位相は使用を始めた時点で判定する（効果で位相が変わっても、ほかの位相の効果は出ない）。
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyList<CardEffect>>? PhaseEffects = null)
{
    /// <summary>使用時の位相名（none / calm / motion）に応じた効果（共通の効果 → 位相ごとの効果）。</summary>
    public IReadOnlyList<CardEffect> EffectsForPhase(string phaseName)
    {
        if (PhaseEffects is null || !PhaseEffects.TryGetValue(phaseName, out var phaseEffects))
            return Effects;
        var all = new List<CardEffect>(Effects.Count + phaseEffects.Count);
        all.AddRange(Effects);
        all.AddRange(phaseEffects);
        return all;
    }

    /// <summary>Backward-compatible interpretation of the old boolean field.</summary>
    public int EffectiveUsesPerTurn => UsesPerTurn > 0 ? UsesPerTurn : (OncePerTurn ? 1 : 0);
}
