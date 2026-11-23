using System;
using System.Collections.Generic;

namespace RoguelikeCardGame.Core.Cards;

/// <summary>
/// Card-level mechanics whose parameters cannot be represented safely by the legacy string
/// keyword list.  These definitions describe authoring data only; battle state belongs to the
/// battle layer.
/// </summary>
public abstract record CardAbilityDefinition(CardAbilityKind Kind);

public enum CardAbilityKind
{
    Finisher,
    Reprise,
    Contingency,
}

/// <summary>終撃。The effect list resolves first, then the current combo is finalized.</summary>
public sealed record FinisherAbilityDefinition()
    : CardAbilityDefinition(CardAbilityKind.Finisher);

/// <summary>再演。Re-executes the most recent actually played card matching <see cref="Filter"/>.</summary>
public sealed record RepriseAbilityDefinition(RepriseCardFilter Filter)
    : CardAbilityDefinition(CardAbilityKind.Reprise);

/// <summary>伏策。The effect list is armed in Exile and resolves when <see cref="Trigger"/> fires.</summary>
public sealed record ContingencyAbilityDefinition(ContingencyTriggerDefinition Trigger)
    : CardAbilityDefinition(CardAbilityKind.Contingency);

/// <summary>
/// Typed and extensible search criteria for 再演. Null/empty members do not constrain that
/// dimension. Original cost means the printed cost of the card version, before battle cost
/// modifiers; paid-cost criteria are kept separate so future cards can opt into them explicitly.
/// RequiredKeywords matches only the card version's ordinary string keyword list; typed
/// abilities and momentum conditions are intentionally not converted into string keywords.
/// </summary>
public sealed record RepriseCardFilter(
    IReadOnlyList<CardType>? CardTypes = null,
    int? MinOriginalCost = null,
    int? MaxOriginalCost = null,
    int? MinPaidCost = null,
    int? MaxPaidCost = null,
    IReadOnlyList<string>? RequiredKeywords = null)
{
    public IReadOnlyList<CardType> EffectiveCardTypes =>
        CardTypes ?? Array.Empty<CardType>();

    public IReadOnlyList<string> EffectiveRequiredKeywords =>
        RequiredKeywords ?? Array.Empty<string>();
}

public enum ContingencyTriggerTiming
{
    TurnStart,
    BeforeEnemyAttack,
    ComboReached,
}

/// <summary>
/// Typed 伏策 trigger. <see cref="ComboThreshold"/> is required only for
/// <see cref="ContingencyTriggerTiming.ComboReached"/>.
/// </summary>
public sealed record ContingencyTriggerDefinition(
    ContingencyTriggerTiming Timing,
    int? ComboThreshold = null);
