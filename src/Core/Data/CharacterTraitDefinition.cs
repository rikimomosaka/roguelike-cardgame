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
    /// <summary>自身の位相が静の状態でターンを終えた時（手札を捨てる前）。</summary>
    OnCalmTurnEnd,
    /// <summary>自身の位相が動になった時（動から動への上書きも含む）。</summary>
    OnMotionEntered,
}

/// <summary>特性の追加の発動条件。1 つの特性が複数の条件でそれぞれ別の効果を出す時に使う。</summary>
public sealed record CharacterTraitClause(
    CharacterTraitTrigger Trigger,
    IReadOnlyList<CardEffect> Effects,
    int Threshold = 0,
    int UsesPerTurn = 0);

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
    string? Description = null,
    IReadOnlyList<CharacterTraitClause>? Clauses = null)
{
    /// <summary>Zero means that the trait has no per-turn activation cap.</summary>
    public bool HasPerTurnLimit => UsesPerTurn > 0;

    /// <summary>
    /// 発動条件の一覧（主の条件 → 追加の条件）。Key は使用回数の記録に使う（主は特性 ID、追加は「ID#番号」）。
    /// </summary>
    public IEnumerable<(string Key, CharacterTraitClause Clause)> AllClauses()
    {
        yield return (Id, new CharacterTraitClause(Trigger, Effects, Threshold, UsesPerTurn));
        if (Clauses is null) yield break;
        for (int i = 0; i < Clauses.Count; i++)
            yield return ($"{Id}#{i + 1}", Clauses[i]);
    }

    /// <summary>この発動条件を持つか。</summary>
    public bool HasTrigger(CharacterTraitTrigger trigger)
    {
        foreach (var (_, clause) in AllClauses())
            if (clause.Trigger == trigger) return true;
        return false;
    }
}
