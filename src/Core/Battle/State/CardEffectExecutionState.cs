using System.Collections.Immutable;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// An immutable view of one real card play.  Effect-only executions such as
/// Reprise and Afterimage use this definition snapshot without paying the
/// original cost, moving the original card, or replaying its card abilities.
/// </summary>
public sealed record PlayedCardEffectSnapshot(
    string CardDefinitionId,
    string CardInstanceId,
    string ActorInstanceId,
    CardType CardType,
    int OriginalCost,
    int PaidCost,
    bool IsUpgraded,
    ImmutableArray<string> Keywords,
    ImmutableArray<CardEffect> Effects,
    long PlaySequence)
{
    public RoguelikeCardGame.Core.Data.CardPoolClass? PoolClass { get; init; }

    /// <summary>
    /// New snapshots persist the source card's effective pool. Legacy
    /// snapshots without that field retain their catalog-defined appearance,
    /// and missing catalog data falls back to Universal.
    /// </summary>
    public RoguelikeCardGame.Core.Data.CardPoolClass EffectivePoolClass(
        CardDefinition? definition) =>
        PoolClass
        ?? definition?.PoolClass
        ?? RoguelikeCardGame.Core.Data.CardPoolClass.Universal;

    /// <summary>
    /// Battle-long completion order used by Reprise.  This is intentionally
    /// independent from <see cref="PlaySequence"/>: an armed Contingency is
    /// played first but only becomes eligible after its delayed effects have
    /// actually finished resolving.
    /// </summary>
    public long ResolutionSequence { get; init; }
}

/// <summary>
/// One Contingency card armed in the normal exhaust pile.  The card remains a
/// regular pile member; moving it out of exhaust disarms this record.
/// </summary>
public sealed record ArmedContingency(
    string CardInstanceId,
    string ActorInstanceId,
    ContingencyTriggerDefinition Trigger,
    long ArmedOrdinal);
