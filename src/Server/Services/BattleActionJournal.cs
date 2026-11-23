using System.Collections.Immutable;
using RoguelikeCardGame.Server.Dtos;

namespace RoguelikeCardGame.Server.Services;

/// <summary>
/// An immutable, presentation-only history of successful battle commands.
/// It is deliberately owned by Server rather than Core: replaying this journal
/// must never execute game rules again.
/// </summary>
public sealed record BattleActionJournal
{
    private static readonly ImmutableDictionary<BattleReplayFeedKey, BattleReplayFeedState>
        EmptyFeedStates = ImmutableDictionary<BattleReplayFeedKey, BattleReplayFeedState>.Empty;

    private BattleActionJournal(
        ImmutableArray<BattleActionRecord> entries,
        ImmutableDictionary<BattleReplayFeedKey, BattleReplayFeedState> feedStates,
        long nextSequence)
    {
        Entries = entries;
        FeedStates = feedStates;
        NextSequence = nextSequence;
    }

    public static BattleActionJournal Empty { get; } = new(
        ImmutableArray<BattleActionRecord>.Empty,
        EmptyFeedStates,
        nextSequence: 1);

    /// <summary>All records, ordered by the battle-global sequence.</summary>
    public ImmutableArray<BattleActionRecord> Entries { get; }

    /// <summary>Latest Ready/completion state for every turn/participant feed.</summary>
    public ImmutableDictionary<BattleReplayFeedKey, BattleReplayFeedState> FeedStates { get; }

    /// <summary>The sequence that will be assigned to the next successful command.</summary>
    public long NextSequence { get; }

    public long LastSequence => NextSequence - 1;

    /// <summary>
    /// Appends one command only after the command has succeeded. Sequence assignment and
    /// feed-state transitions are atomic because the returned journal is a new value.
    /// </summary>
    public BattleActionJournal AppendSuccessfulCommand(BattleActionDraft draft)
        => AppendSuccessfulCommands([draft]);

    /// <summary>
    /// Appends a successful command batch in its authoritative execution order.
    /// This is useful for CPU turns that decide several actions inside one server lock.
    /// </summary>
    public BattleActionJournal AppendSuccessfulCommands(
        IEnumerable<BattleActionDraft> drafts)
    {
        ArgumentNullException.ThrowIfNull(drafts);

        var entries = Entries.ToBuilder();
        var feedStates = FeedStates;
        var nextSequence = NextSequence;
        long? previousRevisionAfter = entries.Count == 0
            ? null
            : entries[^1].RevisionAfter;

        foreach (var source in drafts)
        {
            ArgumentNullException.ThrowIfNull(source);
            Validate(source);
            if (previousRevisionAfter is { } previous
                && source.RevisionBefore < previous)
            {
                throw new InvalidOperationException(
                    $"Battle revision regressed from {previous} to "
                    + $"{source.RevisionBefore} for command '{source.CommandId}'.");
            }
            var record = Snapshot(source, nextSequence);
            entries.Add(record);
            feedStates = ApplyFeedChanges(feedStates, record);
            nextSequence = checked(nextSequence + 1);
            previousRevisionAfter = source.RevisionAfter;
        }

        return new BattleActionJournal(entries.ToImmutable(), feedStates, nextSequence);
    }

    /// <summary>
    /// Returns records matching all supplied filters. Participant and actor matching includes
    /// inbound effects, so a target's replay contains actions performed by somebody else.
    /// </summary>
    public ImmutableArray<BattleActionRecord> Query(BattleActionFilter? filter = null)
    {
        filter ??= BattleActionFilter.All;
        if (filter.AfterSequence < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(filter), "AfterSequence cannot be negative.");
        }

        return Entries
            .Where(entry => entry.Sequence > filter.AfterSequence)
            .Where(entry => filter.ThroughSequence is null
                || entry.Sequence <= filter.ThroughSequence.Value)
            .Where(entry => filter.Turn is null || entry.Turn == filter.Turn.Value)
            .Where(entry => filter.ParticipantId is null
                || IsVisibleToParticipant(entry, filter.ParticipantId))
            .Where(entry => filter.ActorInstanceId is null
                || IsVisibleToActor(entry, filter.ActorInstanceId))
            .ToImmutableArray();
    }

    public BattleReplayFeedState GetFeedState(int turn, string participantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(participantId);
        var key = new BattleReplayFeedKey(turn, participantId);
        return FeedStates.TryGetValue(key, out var state)
            ? state
            : new BattleReplayFeedState(
                Turn: turn,
                ParticipantId: participantId,
                IsReady: false,
                IsComplete: false,
                LastSequence: 0);
    }

    private static bool IsVisibleToParticipant(
        BattleActionRecord entry,
        string participantId) =>
        string.Equals(entry.ParticipantId, participantId, StringComparison.Ordinal)
        || entry.AudienceParticipantIds.Contains(participantId, StringComparer.Ordinal)
        || entry.Effects.Any(effect => string.Equals(
            effect.TargetParticipantId, participantId, StringComparison.Ordinal))
        || entry.Intents.Any(intent => string.Equals(
            intent.TargetParticipantId, participantId, StringComparison.Ordinal))
        || entry.FeedChanges.Any(change => string.Equals(
            change.ParticipantId, participantId, StringComparison.Ordinal));

    private static bool IsVisibleToActor(
        BattleActionRecord entry,
        string actorInstanceId) =>
        entry.IsResolution
        || string.Equals(entry.ActorInstanceId, actorInstanceId, StringComparison.Ordinal)
        || entry.TargetActorInstanceIds.Contains(actorInstanceId, StringComparer.Ordinal)
        || entry.Effects.Any(effect => string.Equals(
            effect.SourceActorInstanceId, actorInstanceId, StringComparison.Ordinal))
        || entry.Effects.Any(effect => string.Equals(
            effect.TargetActorInstanceId, actorInstanceId, StringComparison.Ordinal))
        || entry.Intents.Any(intent => string.Equals(
            intent.TargetActorInstanceId, actorInstanceId, StringComparison.Ordinal));

    private static ImmutableDictionary<BattleReplayFeedKey, BattleReplayFeedState>
        ApplyFeedChanges(
            ImmutableDictionary<BattleReplayFeedKey, BattleReplayFeedState> states,
            BattleActionRecord record)
    {
        if (record.ParticipantId is not null)
        {
            states = EnsureFeed(states, record.Turn, record.ParticipantId, record.Sequence);
        }

        foreach (var participantId in record.AudienceParticipantIds)
        {
            states = EnsureFeed(states, record.Turn, participantId, record.Sequence);
        }

        foreach (var participantId in record.Effects
                     .Select(effect => effect.TargetParticipantId)
                     .Concat(record.Intents.Select(intent => intent.TargetParticipantId))
                     .Where(participantId => !string.IsNullOrWhiteSpace(participantId))
                     .Distinct(StringComparer.Ordinal))
        {
            states = EnsureFeed(states, record.Turn, participantId!, record.Sequence);
        }

        foreach (var change in record.FeedChanges)
        {
            var key = new BattleReplayFeedKey(record.Turn, change.ParticipantId);
            var previous = states.TryGetValue(key, out var current)
                ? current
                : new BattleReplayFeedState(
                    record.Turn, change.ParticipantId, false, false, 0);
            if (previous.IsComplete && change.IsComplete == false)
            {
                throw new InvalidOperationException(
                    $"Completed replay feed '{change.ParticipantId}' for turn {record.Turn} "
                    + "cannot be reopened.");
            }
            var isComplete = change.IsComplete ?? previous.IsComplete;
            var isReady = isComplete
                ? true
                : change.IsReady ?? previous.IsReady;
            states = states.SetItem(key, previous with
            {
                IsReady = isReady,
                IsComplete = isComplete,
                LastSequence = record.Sequence,
            });
        }

        return states;
    }

    private static ImmutableDictionary<BattleReplayFeedKey, BattleReplayFeedState> EnsureFeed(
        ImmutableDictionary<BattleReplayFeedKey, BattleReplayFeedState> states,
        int turn,
        string participantId,
        long sequence)
    {
        var key = new BattleReplayFeedKey(turn, participantId);
        return states.TryGetValue(key, out var current)
            ? states.SetItem(key, current with { LastSequence = sequence })
            : states.Add(key, new BattleReplayFeedState(
                turn, participantId, false, false, sequence));
    }

    private static BattleActionRecord Snapshot(BattleActionDraft source, long sequence)
    {
        var targets = (source.TargetActorInstanceIds ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();
        var audience = (source.AudienceParticipantIds ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();
        var effects = (source.Effects ?? [])
            .Select(effect => effect with
            {
                Card = effect.Card is null
                    ? null
                    : effect.Card with
                    {
                        CostChanges = (effect.Card.CostChanges ?? [])
                            .Select(change => change with { })
                            .ToImmutableArray(),
                    },
            })
            .ToImmutableArray();
        var intents = (source.Intents ?? [])
            .Select(intent => intent with { })
            .ToImmutableArray();
        var feedChanges = (source.FeedChanges ?? [])
            .Select(change => change with { })
            .ToImmutableArray();
        var selectedInstanceIds = (source.SelectedInstanceIds ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();
        var card = source.Card is null
            ? null
            : source.Card with
            {
                CostChanges = (source.Card.CostChanges ?? [])
                    .Select(change => change with { })
                    .ToImmutableArray(),
            };

        return new BattleActionRecord(
            Sequence: sequence,
            EntryId: $"battle-action:{sequence}",
            Turn: source.Turn,
            CommandId: source.CommandId,
            Kind: source.Kind,
            ParticipantId: source.ParticipantId,
            ActorInstanceId: source.ActorInstanceId,
            RevisionBefore: source.RevisionBefore,
            RevisionAfter: source.RevisionAfter,
            Card: card,
            TargetActorInstanceIds: targets,
            AudienceParticipantIds: audience,
            Effects: effects,
            Intents: intents,
            FeedChanges: feedChanges,
            IsResolution: source.IsResolution,
            Note: source.Note,
            SelectedInstanceIds: selectedInstanceIds,
            Ability: source.Ability is null ? null : source.Ability with { },
            StateBefore: source.StateBefore,
            StateAfter: source.StateAfter,
            Source: source.Source is null ? null : source.Source with { },
            ComboCountAfter: source.ComboCountAfter);
    }

    private static void Validate(BattleActionDraft draft)
    {
        if (draft.Turn < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(draft), "Turn cannot be negative.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(draft.CommandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.Kind);
        if (draft.RevisionBefore < 0 || draft.RevisionAfter < draft.RevisionBefore)
        {
            throw new ArgumentOutOfRangeException(
                nameof(draft), "Battle revisions must be non-negative and ordered.");
        }

        if (draft.Card is { } card
            && (card.OriginalCost < 0 || card.CurrentCost < 0 || card.PaidCost < 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(draft), "Card costs in a replay snapshot cannot be negative.");
        }

        if ((draft.Effects ?? []).Any(effect => effect.Card is { } effectCard
            && (effectCard.OriginalCost < 0
                || effectCard.CurrentCost < 0
                || effectCard.PaidCost < 0)))
        {
            throw new ArgumentOutOfRangeException(
                nameof(draft),
                "Effect-card costs in a replay snapshot cannot be negative.");
        }

        if (draft.ComboCountAfter is < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(draft),
                "A recorded post-play combo count must be positive.");
        }

        foreach (var change in draft.FeedChanges ?? [])
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(change.ParticipantId);
        }
    }
}

public sealed record BattleActionFilter(
    int? Turn = null,
    string? ParticipantId = null,
    string? ActorInstanceId = null,
    long AfterSequence = 0,
    long? ThroughSequence = null)
{
    public static BattleActionFilter All { get; } = new();
}

/// <summary>
/// Data captured after a command succeeds but before mutable live state changes again.
/// </summary>
public sealed record BattleActionDraft(
    int Turn,
    string CommandId,
    string Kind,
    string? ParticipantId,
    string? ActorInstanceId,
    long RevisionBefore,
    long RevisionAfter,
    BattleCardDecisionSnapshot? Card = null,
    IReadOnlyList<string>? TargetActorInstanceIds = null,
    IReadOnlyList<string>? AudienceParticipantIds = null,
    IReadOnlyList<BattleActionEffectSnapshot>? Effects = null,
    IReadOnlyList<BattleActionIntentSnapshot>? Intents = null,
    IReadOnlyList<BattleReplayFeedChange>? FeedChanges = null,
    bool IsResolution = false,
    string? Note = null,
    IReadOnlyList<string>? SelectedInstanceIds = null,
    BattleAbilityDecisionSnapshot? Ability = null,
    BattleStateDto? StateBefore = null,
    BattleStateDto? StateAfter = null,
    BattleActionSourceSnapshot? Source = null,
    int? ComboCountAfter = null);

public sealed record BattleActionRecord(
    long Sequence,
    string EntryId,
    int Turn,
    string CommandId,
    string Kind,
    string? ParticipantId,
    string? ActorInstanceId,
    long RevisionBefore,
    long RevisionAfter,
    BattleCardDecisionSnapshot? Card,
    ImmutableArray<string> TargetActorInstanceIds,
    ImmutableArray<string> AudienceParticipantIds,
    ImmutableArray<BattleActionEffectSnapshot> Effects,
    ImmutableArray<BattleActionIntentSnapshot> Intents,
    ImmutableArray<BattleReplayFeedChange> FeedChanges,
    bool IsResolution,
    string? Note,
    ImmutableArray<string> SelectedInstanceIds,
    BattleAbilityDecisionSnapshot? Ability,
    BattleStateDto? StateBefore,
    BattleStateDto? StateAfter,
    BattleActionSourceSnapshot? Source,
    int? ComboCountAfter = null);

/// <summary>
/// Stable source label retained even when a target replay hides the full card
/// or ability payload and renders only a compact inbound-effect row.
/// </summary>
public sealed record BattleActionSourceSnapshot(
    string? ActorInstanceId,
    string ActorDisplayName,
    string ActionKind,
    string? ActionDefinitionId,
    string ActionDisplayName);

/// <summary>Exact card presentation and payable-cost calculation at decision time.</summary>
public sealed record BattleCardDecisionSnapshot(
    string CardInstanceId,
    string DefinitionId,
    string DisplayName,
    string CardType,
    string Rarity,
    bool IsUpgraded,
    int OriginalCost,
    int CurrentCost,
    int PaidCost,
    string Description,
    IReadOnlyList<BattleCardCostChangeSnapshot>? CostChanges = null,
    string? PoolClass = null);

public sealed record BattleCardCostChangeSnapshot(
    string Kind,
    long Value,
    string? SourceId,
    string? SourceName);

/// <summary>Presentation captured before a character skill or potion is consumed.</summary>
public sealed record BattleAbilityDecisionSnapshot(
    string DefinitionId,
    string DisplayName,
    string Kind,
    string Description,
    int EnergyCost,
    int ComboGaugeCost,
    int UsesBefore,
    int UsesPerTurn);

/// <summary>A compact, already-resolved effect used by target-side replay feeds.</summary>
public sealed record BattleActionEffectSnapshot(
    string Kind,
    string? SourceActorInstanceId,
    string? TargetParticipantId,
    string? TargetActorInstanceId,
    int? Amount = null,
    string? StatusId = null,
    string? Description = null,
    int? ActualHpLoss = null,
    int? BlockedAmount = null,
    int? HitIndex = null,
    bool? HitLanded = null,
    string? CardDefinitionId = null,
    string? CardInstanceId = null,
    BattleCardDecisionSnapshot? Card = null);

/// <summary>Intent created by an action. Final combat values remain live-bound in Core.</summary>
public sealed record BattleActionIntentSnapshot(
    string Kind,
    string Scope,
    int Amount,
    int Hits,
    string? TargetParticipantId = null,
    string? TargetActorInstanceId = null);

public sealed record BattleReplayFeedChange(
    string ParticipantId,
    bool? IsReady = null,
    bool? IsComplete = null);

public sealed record BattleReplayFeedKey(int Turn, string ParticipantId);

public sealed record BattleReplayFeedState(
    int Turn,
    string ParticipantId,
    bool IsReady,
    bool IsComplete,
    long LastSequence);
