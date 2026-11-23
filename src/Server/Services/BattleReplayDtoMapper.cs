using RoguelikeCardGame.Server.Dtos;

namespace RoguelikeCardGame.Server.Services;

/// <summary>
/// Projects immutable journal records into either their source/full-card view or the compact
/// inbound-effect view used when watching another participant act on the selected character.
/// </summary>
public static class BattleReplayDtoMapper
{
    public static BattleReplayDto ToDto(
        BattleActionJournal journal,
        BattleActionFilter? filter = null)
    {
        ArgumentNullException.ThrowIfNull(journal);
        filter ??= BattleActionFilter.All;
        var records = journal.Query(filter);
        var entries = records
            .Select(record => ToEntryDto(record, filter))
            .ToList();
        var feeds = journal.FeedStates.Values
            .Where(feed => filter.Turn is null || feed.Turn == filter.Turn.Value)
            .Where(feed => filter.ParticipantId is null
                || string.Equals(
                    feed.ParticipantId, filter.ParticipantId, StringComparison.Ordinal))
            .OrderBy(feed => feed.Turn)
            .ThenBy(feed => feed.ParticipantId, StringComparer.Ordinal)
            .Select(ToFeedDto)
            .ToList();

        var (isReady, isComplete) = AggregateFeedState(journal, feeds, filter);
        return new BattleReplayDto(
            FromSequenceExclusive: filter.AfterSequence,
            LatestSequence: journal.LastSequence,
            ReturnedThroughSequence: records.IsEmpty
                ? filter.AfterSequence
                : records[^1].Sequence,
            Turn: filter.Turn,
            ParticipantId: filter.ParticipantId,
            ActorInstanceId: filter.ActorInstanceId,
            FeedState: ToFeedState(isReady, isComplete),
            IsReady: isReady,
            IsComplete: isComplete,
            ParticipantFeeds: feeds,
            Entries: entries);
    }

    private static BattleReplayEntryDto ToEntryDto(
        BattleActionRecord record,
        BattleActionFilter filter)
    {
        var isSourceView = record.IsResolution
            || filter.ParticipantId is null
            || string.Equals(
                record.ParticipantId, filter.ParticipantId, StringComparison.Ordinal);
        if (!record.IsResolution && filter.ActorInstanceId is not null)
        {
            isSourceView &= string.Equals(
                record.ActorInstanceId,
                filter.ActorInstanceId,
                StringComparison.Ordinal);
        }

        var effects = record.Effects
            .Where(effect => isSourceView || IsEffectVisible(effect, filter))
            .Select(effect => new BattleReplayEffectDto(
                Kind: effect.Kind,
                SourceActorInstanceId: effect.SourceActorInstanceId,
                TargetParticipantId: effect.TargetParticipantId,
                TargetActorInstanceId: effect.TargetActorInstanceId,
                Amount: effect.Amount,
                StatusId: effect.StatusId,
                Description: effect.Description,
                ActualHpLoss: effect.ActualHpLoss,
                BlockedAmount: effect.BlockedAmount,
                HitIndex: effect.HitIndex,
                HitLanded: effect.HitLanded,
                CardDefinitionId: effect.CardDefinitionId,
                CardInstanceId: effect.CardInstanceId,
                Card: ToCardDto(effect.Card)))
            .ToList();
        var intents = record.Intents
            .Where(intent => isSourceView || IsIntentVisible(intent, filter))
            .Select(intent => new BattleReplayIntentDto(
                Kind: intent.Kind,
                Scope: intent.Scope,
                Amount: intent.Amount,
                Hits: intent.Hits,
                TargetParticipantId: intent.TargetParticipantId,
                TargetActorInstanceId: intent.TargetActorInstanceId))
            .ToList();

        IReadOnlyList<string> visibleTargetActorIds = isSourceView
            ? record.TargetActorInstanceIds
            : effects.Select(effect => effect.TargetActorInstanceId)
                .Concat(intents.Select(intent => intent.TargetActorInstanceId))
                .Where(actorId => !string.IsNullOrWhiteSpace(actorId))
                .Select(actorId => actorId!)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        var feedChanges = record.FeedChanges
            .Where(change => isSourceView
                || filter.ParticipantId is null
                || string.Equals(
                    change.ParticipantId, filter.ParticipantId, StringComparison.Ordinal))
            .Select(change => new BattleReplayFeedChangeDto(
                change.ParticipantId, change.IsReady, change.IsComplete))
            .ToList();

        return new BattleReplayEntryDto(
            Sequence: record.Sequence,
            EntryId: record.EntryId,
            Turn: record.Turn,
            CommandId: record.CommandId,
            Kind: record.Kind,
            ViewKind: ResolveViewKind(
                record, isSourceView, effects.Count, intents.Count, feedChanges.Count),
            ParticipantId: record.ParticipantId,
            ActorInstanceId: record.ActorInstanceId,
            RevisionBefore: record.RevisionBefore,
            RevisionAfter: record.RevisionAfter,
            Card: isSourceView ? ToCardDto(record.Card) : null,
            TargetActorInstanceIds: visibleTargetActorIds,
            Effects: effects,
            Intents: intents,
            FeedChanges: feedChanges,
            IsResolution: record.IsResolution,
            Note: record.Note,
            SelectedInstanceIds: isSourceView
                ? record.SelectedInstanceIds
                : [],
            Ability: isSourceView ? ToAbilityDto(record.Ability) : null,
            StateBefore: record.StateBefore,
            StateAfter: record.StateAfter,
            Source: record.Source is null
                ? null
                : new BattleReplaySourceDto(
                    record.Source.ActorInstanceId,
                    record.Source.ActorDisplayName,
                    record.Source.ActionKind,
                    record.Source.ActionDefinitionId,
                    record.Source.ActionDisplayName),
            ComboCountAfter: record.ComboCountAfter);
    }

    private static string ResolveViewKind(
        BattleActionRecord record,
        bool isSourceView,
        int visibleEffectCount,
        int visibleIntentCount,
        int visibleFeedChangeCount)
    {
        if (record.IsResolution)
        {
            return "resolution";
        }

        if (visibleFeedChangeCount > 0
            && visibleEffectCount == 0
            && visibleIntentCount == 0
            && (!isSourceView || record.Card is null))
        {
            return "feedState";
        }

        return isSourceView ? "action" : "externalEffect";
    }

    private static bool IsEffectVisible(
        BattleActionEffectSnapshot effect,
        BattleActionFilter filter) =>
        (filter.ParticipantId is null
            || string.Equals(
                effect.TargetParticipantId, filter.ParticipantId, StringComparison.Ordinal))
        && (filter.ActorInstanceId is null
            || string.Equals(
                effect.TargetActorInstanceId, filter.ActorInstanceId, StringComparison.Ordinal));

    private static bool IsIntentVisible(
        BattleActionIntentSnapshot intent,
        BattleActionFilter filter) =>
        (filter.ParticipantId is null
            || string.Equals(
                intent.TargetParticipantId, filter.ParticipantId, StringComparison.Ordinal))
        && (filter.ActorInstanceId is null
            || string.Equals(
                intent.TargetActorInstanceId, filter.ActorInstanceId, StringComparison.Ordinal));

    internal static BattleReplayCardDto? ToCardDto(BattleCardDecisionSnapshot? card) =>
        card is null
            ? null
            : new BattleReplayCardDto(
                CardInstanceId: card.CardInstanceId,
                DefinitionId: card.DefinitionId,
                DisplayName: card.DisplayName,
                CardType: card.CardType,
                Rarity: card.Rarity,
                IsUpgraded: card.IsUpgraded,
                OriginalCost: card.OriginalCost,
                CurrentCost: card.CurrentCost,
                PaidCost: card.PaidCost,
                Description: card.Description,
                CostChanges: (card.CostChanges ?? [])
                    .Select(change => new BattleReplayCardCostChangeDto(
                        change.Kind,
                        change.Value,
                        change.SourceId,
                        change.SourceName))
                    .ToList(),
                PoolClass: card.PoolClass);

    private static BattleReplayAbilityDto? ToAbilityDto(
        BattleAbilityDecisionSnapshot? ability) => ability is null
        ? null
        : new BattleReplayAbilityDto(
            ability.DefinitionId,
            ability.DisplayName,
            ability.Kind,
            ability.Description,
            ability.EnergyCost,
            ability.ComboGaugeCost,
            ability.UsesBefore,
            ability.UsesPerTurn);

    private static BattleReplayParticipantFeedDto ToFeedDto(BattleReplayFeedState feed) =>
        new(
            Turn: feed.Turn,
            ParticipantId: feed.ParticipantId,
            State: ToFeedState(feed.IsReady, feed.IsComplete),
            IsReady: feed.IsReady,
            IsComplete: feed.IsComplete,
            LastSequence: feed.LastSequence);

    private static (bool IsReady, bool IsComplete) AggregateFeedState(
        BattleActionJournal journal,
        IReadOnlyList<BattleReplayParticipantFeedDto> feeds,
        BattleActionFilter filter)
    {
        if (filter.Turn is not null && filter.ParticipantId is not null)
        {
            var feed = journal.GetFeedState(filter.Turn.Value, filter.ParticipantId);
            return (feed.IsReady, feed.IsComplete);
        }

        if (feeds.Count == 0)
        {
            return (false, false);
        }

        return (
            feeds.All(feed => feed.IsReady),
            feeds.All(feed => feed.IsComplete));
    }

    private static string ToFeedState(bool isReady, bool isComplete) =>
        isComplete ? "complete" : isReady ? "ready" : "live";
}
