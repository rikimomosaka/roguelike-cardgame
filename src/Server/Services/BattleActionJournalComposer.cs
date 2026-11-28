using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;

namespace RoguelikeCardGame.Server.Services;

/// <summary>
/// Converts already accepted Core commands into immutable presentation
/// snapshots. It never executes or re-implements a game rule.
/// </summary>
internal static class BattleActionJournalComposer
{
    public static BattleActionDraft CardPlay(
        BattleState before,
        BattleState after,
        string participantId,
        int handIndex,
        IReadOnlyList<BattleEvent> events,
        DataCatalog catalog,
        string commandId)
    {
        var participant = GetParticipant(before, participantId);
        var participantAfter = BattleParticipantAccess.TryGet(
            after, participantId, out var committedParticipant)
            ? committedParticipant
            : participant;
        BattleCardInstance? card = handIndex >= 0 && handIndex < participant.Hand.Length
            ? participant.Hand[handIndex]
            : handIndex >= 0 && handIndex < before.Hand.Length
                ? before.Hand[handIndex]
                : null;
        var playEvent = events.FirstOrDefault(entry =>
                entry.Kind == BattleEventKind.PlayCard
                && string.Equals(entry.CasterInstanceId, participant.ActorInstanceId,
                    StringComparison.Ordinal));
        int paidCost = playEvent?.Amount ?? 0;

        var targets = card is null
            ? ResolveTargets(after, participantAfter)
            : ResolveCardTargets(after, participantAfter, card, catalog);
        var effects = SnapshotEffects(events, after, before, catalog);
        var intents = SnapshotActorIntentDelta(
            before, after, participant.ActorInstanceId);
        var cardDecision = card is null
            ? null
            : SnapshotCard(before, participant, card, paidCost, catalog);
        return new BattleActionDraft(
            Turn: before.Turn,
            CommandId: commandId,
            Kind: "playCard",
            ParticipantId: participant.ParticipantId,
            ActorInstanceId: participant.ActorInstanceId,
            RevisionBefore: before.ActionRevision,
            RevisionAfter: after.ActionRevision,
            Card: cardDecision,
            TargetActorInstanceIds: targets,
            AudienceParticipantIds: AudienceForAction(
                after, participant.ParticipantId, targets, effects, intents),
            Effects: effects,
            Intents: intents,
            StateBefore: BattleStateDtoMapper.ToDto(before, catalog),
            StateAfter: BattleStateDtoMapper.ToDto(after, catalog),
            Source: CreateSource(
                before,
                participant,
                catalog,
                actionKind: "card",
                actionDefinitionId: cardDecision?.DefinitionId,
                actionDisplayName: cardDecision?.DisplayName ?? "Card"),
            ComboCountAfter: playEvent?.ComboCountAfter);
    }

    public static BattleActionDraft Command(
        BattleState before,
        BattleState after,
        string kind,
        string? participantId,
        IReadOnlyList<BattleEvent> events,
        string commandId,
        IReadOnlyList<BattleReplayFeedChange>? feedChanges = null,
        bool isResolution = false,
        string? note = null,
        IReadOnlyList<string>? selectedInstanceIds = null,
        BattleAbilityDecisionSnapshot? ability = null,
        long? revisionBefore = null,
        long? revisionAfter = null,
        IReadOnlyList<string>? targetActorInstanceIds = null,
        DataCatalog? catalog = null)
    {
        BattleParticipantState? participant = null;
        if (participantId is not null)
        {
            if (!BattleParticipantAccess.TryGet(after, participantId, out participant!))
                BattleParticipantAccess.TryGet(before, participantId, out participant!);
        }

        var effects = SnapshotEffects(events, after, before, catalog);
        var intents = participant is null
            ? Array.Empty<BattleActionIntentSnapshot>()
            : SnapshotActorIntentDelta(
                before, after, participant.ActorInstanceId).ToArray();
        var targets = targetActorInstanceIds?.ToArray()
            ?? effects.Select(effect => effect.TargetActorInstanceId)
                .Concat(intents.Select(intent => intent.TargetActorInstanceId))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

        return new BattleActionDraft(
            Turn: before.Turn,
            CommandId: commandId,
            Kind: kind,
            ParticipantId: participantId,
            ActorInstanceId: participant?.ActorInstanceId,
            RevisionBefore: revisionBefore ?? before.ActionRevision,
            RevisionAfter: revisionAfter
                ?? Math.Max(before.ActionRevision, after.ActionRevision),
            TargetActorInstanceIds: targets,
            AudienceParticipantIds: isResolution
                ? AllParticipantIds(after)
                : AudienceForAction(
                    after, participantId, targets, effects, intents),
            Effects: effects,
            Intents: intents,
            FeedChanges: feedChanges,
            IsResolution: isResolution,
            Note: note,
            SelectedInstanceIds: selectedInstanceIds,
            Ability: ability,
            StateBefore: catalog is null
                ? null
                : BattleStateDtoMapper.ToDto(before, catalog),
            StateAfter: catalog is null
                ? null
                : BattleStateDtoMapper.ToDto(after, catalog),
            Source: CreateSource(
                before,
                participant,
                catalog,
                actionKind: ability?.Kind ?? kind,
                actionDefinitionId: ability?.DefinitionId ?? note,
                actionDisplayName: ability?.DisplayName ?? kind));
    }

    public static BattleActionDraft CharacterSkill(
        BattleState before,
        BattleState after,
        string participantId,
        string skillId,
        IReadOnlyList<BattleEvent> events,
        DataCatalog catalog,
        string commandId)
    {
        var participant = GetParticipant(before, participantId);
        BattleAbilityDecisionSnapshot? ability = null;
        IReadOnlyList<string> targets = [];
        IReadOnlyList<CharacterSkillDefinition>? availableSkills =
            catalog.TryGetCharacter(participant.CharacterId, out var character)
                ? character.EffectiveSkills
                : catalog.TryGetUnit(participant.CharacterId, out var unit)
                    ? unit.EffectiveSkills
                    : null;
        if (availableSkills is not null)
        {
            var skill = availableSkills.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, skillId, StringComparison.Ordinal));
            if (skill is not null)
            {
                var context = ActorContext(before, participant.ActorInstanceId);
                int uses = participant.CharacterSkillUsesThisTurn
                    .GetValueOrDefault(skill.Id);
                ability = new BattleAbilityDecisionSnapshot(
                    DefinitionId: skill.Id,
                    DisplayName: skill.Name,
                    Kind: "characterSkill",
                    Description: CardTextFormatter.FormatUsableEffects(
                        skill.Effects,
                        skill.Keywords,
                        context,
                        skill.EffectiveUsesPerTurn,
                        uses),
                    EnergyCost: skill.Cost.Energy,
                    ComboGaugeCost: skill.Cost.ComboGauge,
                    UsesBefore: uses,
                    UsesPerTurn: skill.EffectiveUsesPerTurn);
                var participantAfter = BattleParticipantAccess.TryGet(
                    after, participantId, out var committed)
                    ? committed
                    : participant;
                targets = ResolveEffectTargets(after, participantAfter, skill.Effects);
            }
        }

        return Command(
            before, after, "useCharacterSkill", participantId, events, commandId,
            note: skillId,
            ability: ability,
            targetActorInstanceIds: targets,
            catalog: catalog);
    }

    public static BattleActionDraft Potion(
        BattleState before,
        BattleState after,
        string participantId,
        int potionIndex,
        IReadOnlyList<BattleEvent> events,
        DataCatalog catalog,
        string commandId)
    {
        var participant = GetParticipant(before, participantId);
        BattleAbilityDecisionSnapshot? ability = null;
        IReadOnlyList<string> targets = [];
        if (potionIndex >= 0
            && potionIndex < participant.Potions.Length
            && catalog.TryGetPotion(participant.Potions[potionIndex], out var potion))
        {
            int uses = participant.PotionUsesThisTurn.GetValueOrDefault(potion.Id);
            ability = new BattleAbilityDecisionSnapshot(
                DefinitionId: potion.Id,
                DisplayName: potion.Name,
                Kind: "potion",
                Description: CardTextFormatter.FormatUsableEffects(
                    potion.Effects,
                    keywords: null,
                    ActorContext(before, participant.ActorInstanceId),
                    potion.UsesPerTurn,
                    uses),
                EnergyCost: 0,
                ComboGaugeCost: 0,
                UsesBefore: uses,
                UsesPerTurn: potion.UsesPerTurn);
            var participantAfter = BattleParticipantAccess.TryGet(
                after, participantId, out var committed)
                ? committed
                : participant;
            targets = ResolveEffectTargets(after, participantAfter, potion.Effects);
        }

        return Command(
            before, after, "usePotion", participantId, events, commandId,
            ability: ability,
            targetActorInstanceIds: targets,
            catalog: catalog);
    }

    /// <summary>
    /// ターン開始時に敵が計画したカード使用を、味方CPUと同じ形式の行動記録にする。
    /// 敵の予約は予測のみなので、記録の「予定」は戦場へまだ反映されていない。
    /// </summary>
    /// <param name="minRevision">
    /// この記録の直前の記録の RevisionAfter。敵の計画はコマンドの途中（版番号を進める前）で行われることがあるので、
    /// 記録の版番号がこれより古くならないようにそろえる。
    /// </param>
    public static IReadOnlyList<BattleActionDraft> EnemyPlanning(
        BattleState stateAfterEnemyPlanning,
        DataCatalog catalog,
        string commandPrefix,
        long minRevision = 0)
    {
        var actions = stateAfterEnemyPlanning.EnemyPlanningActions;
        if (actions.IsDefaultOrEmpty)
            return Array.Empty<BattleActionDraft>();
        var before = actions[0].StateBefore ?? stateAfterEnemyPlanning;
        // 計画は毎ターンの開始時に作り直される。今のターンの計画でなければ（ターン開始の途中で
        // 選択待ちになり、まだ計画していない時など）、前のターンの記録済みの計画なので記録しない。
        if (before.Turn != stateAfterEnemyPlanning.Turn)
            return Array.Empty<BattleActionDraft>();
        var planning = new BattleCpuPlanningResult(
            stateAfterEnemyPlanning,
            actions,
            ImmutableArray<CpuPlanningStop>.Empty,
            ImmutableArray<string>.Empty)
        {
            StateBeforePlanning = before,
        };
        var drafts = CpuPlanning(before, planning, catalog, $"{commandPrefix}:enemy")
            .Select(draft =>
            {
                long revisionBefore = Math.Max(draft.RevisionBefore, minRevision);
                return draft with
                {
                    RevisionBefore = revisionBefore,
                    RevisionAfter = Math.Max(draft.RevisionAfter, revisionBefore),
                };
            })
            .ToList();

        // 敵の計画はターン開始時に完結しているため、各敵の最後の記録で
        // 再生フィードを完了（READY + COMPLETE）にする。これが無いと
        // クライアントの再生が LIVE のまま次の行動を待ち続ける。
        var lastIndexByParticipant = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < drafts.Count; index++)
        {
            if (drafts[index].ParticipantId is { } participantId)
                lastIndexByParticipant[participantId] = index;
        }
        foreach (var (participantId, index) in lastIndexByParticipant)
        {
            var draft = drafts[index];
            drafts[index] = draft with
            {
                FeedChanges = (draft.FeedChanges ?? Array.Empty<BattleReplayFeedChange>())
                    .Append(new BattleReplayFeedChange(
                        participantId, IsReady: true, IsComplete: true))
                    .ToArray(),
            };
        }
        return drafts;
    }

    public static IReadOnlyList<BattleActionDraft> CpuPlanning(
        BattleState before,
        BattleCpuPlanningResult planning,
        DataCatalog catalog,
        string commandPrefix)
    {
        var drafts = new List<BattleActionDraft>();
        long revision = Math.Max(0, before.ActionRevision);
        int journalOrdinal = 0;
        var timeline = MergeCpuPlanningTimeline(planning);

        foreach (var item in timeline)
        {
            if (item.Action is { } action)
            {
                var stateBefore = action.StateBefore
                    ?? planning.StateBeforePlanning
                    ?? before;
                var stateAfter = action.StateAfter ?? planning.State;
                var effects = SnapshotEffects(
                    action.Events, stateAfter, stateBefore, catalog);
                var intents = SnapshotCommittedIntents(action, stateAfter);
                var targets = action.TargetActorInstanceIds.IsDefault
                    ? ResolveTargets(
                        stateAfter,
                        action.TargetEnemyIndex,
                        action.TargetAllyIndex)
                    : action.TargetActorInstanceIds;
                var cardDecision = SnapshotCpuCard(action, catalog);
                int? comboCountAfter = action.Events.FirstOrDefault(entry =>
                    entry.Kind == BattleEventKind.PlayCard
                    && string.Equals(
                        entry.CasterInstanceId,
                        action.ActorInstanceId,
                        StringComparison.Ordinal))?.ComboCountAfter;
                string? replayEnemyParticipantId = null;
                if (!BattleParticipantAccess.TryGet(
                        stateBefore, action.ParticipantId, out var sourceParticipant))
                {
                    // カード制御の敵の計画行動（BattleState.EnemyPlanningActions）。
                    // 再生時に敵の手札・山札・エナジーをプレイヤーUIの位置へ表示するため、
                    // 記録の前後状態にだけ敵参加者のカード資源を含める。
                    if (BattleParticipantAccess.TryGetEnemy(
                            stateBefore, action.ParticipantId, out sourceParticipant))
                    {
                        replayEnemyParticipantId = action.ParticipantId;
                    }
                }
                drafts.Add(new BattleActionDraft(
                    Turn: action.Turn > 0 ? action.Turn : stateBefore.Turn,
                    CommandId: $"{commandPrefix}:cpu:{journalOrdinal++}",
                    Kind: "playCard",
                    ParticipantId: action.ParticipantId,
                    ActorInstanceId: action.ActorInstanceId,
                    RevisionBefore: action.RevisionBefore,
                    RevisionAfter: action.RevisionAfter,
                    Card: cardDecision,
                    TargetActorInstanceIds: targets,
                    AudienceParticipantIds: AudienceForAction(
                        stateAfter,
                        action.ParticipantId,
                        targets,
                        effects,
                        intents),
                    Effects: effects,
                    Intents: intents,
                    FeedChanges: action.ReopenedParticipantIds
                        .Select(id => new BattleReplayFeedChange(
                            id, IsReady: false, IsComplete: null))
                        .ToArray(),
                    SelectedInstanceIds: action.SelectedInstanceIds,
                    StateBefore: BattleStateDtoMapper.ToDto(
                        stateBefore, catalog, replayEnemyParticipantId),
                    StateAfter: BattleStateDtoMapper.ToDto(
                        stateAfter, catalog, replayEnemyParticipantId),
                    Source: CreateSource(
                        stateBefore,
                        sourceParticipant,
                        catalog,
                        actionKind: "card",
                        actionDefinitionId: cardDecision.DefinitionId,
                        actionDisplayName: cardDecision.DisplayName),
                    ComboCountAfter: comboCountAfter));
                revision = Math.Max(revision, action.RevisionAfter);
                continue;
            }

            var stop = item.Stop!;
            var stateBeforeStop = stop.StateBefore ?? planning.State;
            var stateAfterStop = stop.StateAfter ?? planning.State;
            BattleParticipantState? participant = null;
            bool participantStillExists = BattleParticipantAccess.TryGet(
                stateAfterStop, stop.ParticipantId, out participant!);
            if (!participantStillExists)
            {
                BattleParticipantAccess.TryGet(
                    stateBeforeStop, stop.ParticipantId, out participant!);
            }
            long revisionBefore = stop.StateBefore is null
                ? Math.Max(revision, planning.State.ActionRevision)
                : Math.Max(0, stateBeforeStop.ActionRevision);
            long revisionAfter = stop.StateAfter is null
                ? revisionBefore
                : Math.Max(revisionBefore, stateAfterStop.ActionRevision);
            drafts.Add(new BattleActionDraft(
                Turn: stateBeforeStop.Turn,
                CommandId: $"{commandPrefix}:cpu-ready:{journalOrdinal++}",
                Kind: "ready",
                ParticipantId: stop.ParticipantId,
                ActorInstanceId: participant?.ActorInstanceId,
                RevisionBefore: revisionBefore,
                RevisionAfter: revisionAfter,
                FeedChanges:
                [
                    new BattleReplayFeedChange(
                        stop.ParticipantId,
                        IsReady: true,
                        // A summon removed during planning will never appear
                        // in the later all-ready participant set. Close its
                        // feed at the exact ActorUnavailable stop instead of
                        // leaving replay polling permanently in READY.
                        IsComplete: participantStillExists ? null : true),
                ],
                Note: stop.Reason.ToString(),
                StateBefore: BattleStateDtoMapper.ToDto(stateBeforeStop, catalog),
                StateAfter: BattleStateDtoMapper.ToDto(stateAfterStop, catalog),
                Source: CreateSource(
                    stateBeforeStop,
                    participant,
                    catalog,
                    actionKind: "ready",
                    actionDefinitionId: null,
                    actionDisplayName: "Ready")));
            revision = Math.Max(revision, revisionAfter);
        }

        return drafts;
    }

    private static IReadOnlyList<CpuPlanningTimelineItem> MergeCpuPlanningTimeline(
        BattleCpuPlanningResult planning)
    {
        var items = planning.Actions
            .Select((action, index) => new CpuPlanningTimelineItem(
                action.PlanningOrdinal, index, action, Stop: null))
            .Concat(planning.Stops.Select((stop, index) =>
                new CpuPlanningTimelineItem(
                    stop.PlanningOrdinal,
                    planning.Actions.Length + index,
                    Action: null,
                    stop)))
            .ToList();

        // Results constructed before PlanningOrdinal existed keep the historic
        // actions-then-stops order. Coordinator-produced results always carry a
        // complete, unique ordinal sequence and are merged chronologically.
        bool hasCompleteTimeline = items.All(item => item.PlanningOrdinal >= 0)
            && items.Select(item => item.PlanningOrdinal).Distinct().Count() == items.Count;
        return hasCompleteTimeline
            ? items.OrderBy(item => item.PlanningOrdinal).ToArray()
            : items.OrderBy(item => item.LegacyOrdinal).ToArray();
    }

    private sealed record CpuPlanningTimelineItem(
        long PlanningOrdinal,
        int LegacyOrdinal,
        CpuCommittedAction? Action,
        CpuPlanningStop? Stop);

    private static BattleActionSourceSnapshot CreateSource(
        BattleState state,
        BattleParticipantState? participant,
        DataCatalog? catalog,
        string actionKind,
        string? actionDefinitionId,
        string actionDisplayName)
    {
        string? actorInstanceId = participant?.ActorInstanceId;
        string definitionId = participant?.CharacterId
            ?? state.Allies.FirstOrDefault(actor => string.Equals(
                actor.InstanceId, actorInstanceId, StringComparison.Ordinal))?.DefinitionId
            ?? string.Empty;
        string actorDisplayName = definitionId;
        if (catalog is not null)
        {
            if (catalog.TryGetCharacter(definitionId, out var character))
                actorDisplayName = character.Name;
            else if (catalog.TryGetUnit(definitionId, out var unit))
                actorDisplayName = unit.Name;
            else if (catalog.TryGetEnemy(definitionId, out var enemy))
                actorDisplayName = enemy.Name;
        }
        if (string.IsNullOrWhiteSpace(actorDisplayName))
            actorDisplayName = actorInstanceId ?? "System";

        return new BattleActionSourceSnapshot(
            ActorInstanceId: actorInstanceId,
            ActorDisplayName: actorDisplayName,
            ActionKind: actionKind,
            ActionDefinitionId: actionDefinitionId,
            ActionDisplayName: actionDisplayName);
    }

    private static BattleCardDecisionSnapshot SnapshotCard(
        BattleState state,
        BattleParticipantState participant,
        BattleCardInstance card,
        int paidCost,
        DataCatalog catalog,
        bool isInHand = true)
    {
        if (!catalog.TryGetCard(card.CardDefinitionId, out var definition))
            return SnapshotCatalogCard(
                card.InstanceId, card.CardDefinitionId, paidCost, catalog,
                card);

        var owner = state.Allies.FirstOrDefault(actor => string.Equals(
            actor.InstanceId, participant.ActorInstanceId, StringComparison.Ordinal));
        var projected = state.Participants.IsDefaultOrEmpty
            ? state
            : BattleParticipantAccess.ProjectToRoot(state, participant.ParticipantId);
        var breakdown = isInHand && owner is not null
            ? BattleCardCostCalculator.EvaluateForHand(
                projected, card, definition, owner)
            : BattleCardCostCalculator.Evaluate(
                card, definition, owner: null, isInHand: false,
                comboReductionEligible: false);
        var context = owner is null
            ? CardActorContext.Empty
            : new CardActorContext(
                owner.GetStatus("strength"),
                owner.GetStatus("weak"),
                owner.GetStatus("dexterity"))
            {
                AttackPower = owner.AttackPower,
                DefensePower = owner.DefensePower,
            };
        int uses = CardUsageTracker.Count(projected, definition.Id);

        return new BattleCardDecisionSnapshot(
            CardInstanceId: card.InstanceId,
            DefinitionId: definition.Id,
            DisplayName: definition.DisplayName ?? definition.Name,
            CardType: definition.CardType.ToString(),
            Rarity: definition.Rarity.ToString(),
            IsUpgraded: card.IsUpgraded,
            OriginalCost: breakdown.OriginalCost ?? 0,
            CurrentCost: breakdown.PayableCost ?? Math.Max(0, paidCost),
            PaidCost: Math.Max(0, paidCost),
            Description: CardTextFormatter.FormatWithUsageCount(
                definition, card.IsUpgraded, context, uses),
            CostChanges: breakdown.Changes.Select(change =>
                new BattleCardCostChangeSnapshot(
                    change.Kind.ToString(),
                    change.Value,
                    change.SourceId,
                    change.SourceId)).ToImmutableArray(),
            PoolClass: card.EffectivePoolClass(definition).ToString());
    }

    private static BattleCardDecisionSnapshot SnapshotCpuCard(
        CpuCommittedAction action,
        DataCatalog catalog)
    {
        if (action.CardSnapshot is not { } card
            || action.CostBreakdown is not { } breakdown
            || !catalog.TryGetCard(action.CardDefinitionId, out var definition))
        {
            return SnapshotCatalogCard(
                action.CardInstanceId,
                action.CardDefinitionId,
                action.PaidEnergy,
                catalog,
                action.CardSnapshot);
        }

        var context = new CardActorContext(
            action.ActorContext.Strength,
            action.ActorContext.Weak,
            action.ActorContext.Dexterity)
        {
            AttackPower = action.ActorContext.AttackPower,
            DefensePower = action.ActorContext.DefensePower,
        };
        return new BattleCardDecisionSnapshot(
            CardInstanceId: card.InstanceId,
            DefinitionId: definition.Id,
            DisplayName: definition.DisplayName ?? definition.Name,
            CardType: definition.CardType.ToString(),
            Rarity: definition.Rarity.ToString(),
            IsUpgraded: card.IsUpgraded,
            OriginalCost: breakdown.OriginalCost ?? 0,
            CurrentCost: breakdown.PayableCost ?? Math.Max(0, action.PaidEnergy),
            PaidCost: Math.Max(0, action.PaidEnergy),
            Description: CardTextFormatter.FormatWithUsageCount(
                definition, card.IsUpgraded, context, action.UsesBefore),
            CostChanges: breakdown.Changes.Select(change =>
                new BattleCardCostChangeSnapshot(
                    change.Kind.ToString(),
                    change.Value,
                    change.SourceId,
                    change.SourceId)).ToImmutableArray(),
            PoolClass: card.EffectivePoolClass(definition).ToString());
    }

    private static BattleCardDecisionSnapshot SnapshotCatalogCard(
        string instanceId,
        string definitionId,
        int paidCost,
        DataCatalog catalog,
        BattleCardInstance? card = null)
    {
        if (!catalog.TryGetCard(definitionId, out var definition))
        {
            return new BattleCardDecisionSnapshot(
                instanceId, definitionId, definitionId, "Unknown", "Unknown",
                false, Math.Max(0, paidCost), Math.Max(0, paidCost),
                Math.Max(0, paidCost), string.Empty,
                PoolClass: (card?.EffectivePoolClass(definition: null)
                    ?? CardPoolClass.Universal).ToString());
        }

        int originalCost = definition.Cost ?? 0;
        return new BattleCardDecisionSnapshot(
            instanceId,
            definition.Id,
            definition.DisplayName ?? definition.Name,
            definition.CardType.ToString(),
            definition.Rarity.ToString(),
            IsUpgraded: false,
            OriginalCost: originalCost,
            CurrentCost: Math.Max(0, paidCost),
            PaidCost: Math.Max(0, paidCost),
            Description: CardTextFormatter.Format(definition, upgraded: false),
            PoolClass: (card?.EffectivePoolClass(definition)
                ?? definition.PoolClass
                ?? CardPoolClass.Universal).ToString());
    }

    private static IReadOnlyList<BattleActionEffectSnapshot> SnapshotEffects(
        IReadOnlyList<BattleEvent> events,
        BattleState stateAfter,
        BattleState? stateBefore = null,
        DataCatalog? catalog = null) => events
        .Where(entry => entry.Kind is not BattleEventKind.PlayCard
            and not BattleEventKind.ParticipantReadyChanged
            and not BattleEventKind.ResolutionStarted)
        .Select(entry => new BattleActionEffectSnapshot(
            Kind: entry.Kind.ToString(),
            SourceActorInstanceId: entry.CasterInstanceId
                ?? entry.Source?.ActorInstanceId,
            TargetParticipantId: ParticipantIdForActor(
                    stateAfter, entry.TargetInstanceId)
                ?? (stateBefore is null
                    ? null
                    : ParticipantIdForActor(stateBefore, entry.TargetInstanceId)),
            TargetActorInstanceId: entry.TargetInstanceId,
            Amount: entry.Kind == BattleEventKind.DealDamage
                ? entry.ActualHpLoss ?? entry.Amount
                : entry.Amount,
            StatusId: entry.Kind is BattleEventKind.ApplyStatus
                or BattleEventKind.RemoveStatus ? entry.Note : null,
            Description: entry.Note,
            ActualHpLoss: entry.ActualHpLoss,
            BlockedAmount: entry.BlockedAmount,
            HitIndex: entry.HitIndex,
            HitLanded: entry.HitLanded,
            CardDefinitionId: entry.ReenactedCard?.CardDefinitionId
                ?? entry.CardId
                ?? entry.Source?.DefinitionId,
            CardInstanceId: entry.ReenactedCard?.CardInstanceId
                ?? entry.Source?.InstanceId,
            Card: (entry.Kind is BattleEventKind.ReenactCardEffects
                or BattleEventKind.ContingencyTriggered
                or BattleEventKind.ContingencyResolved)
                && catalog is not null
                ? SnapshotReenactedCard(entry, catalog)
                : null))
        .ToImmutableArray();

    internal static BattleCardDecisionSnapshot? SnapshotReenactedCard(
        BattleEvent battleEvent,
        DataCatalog catalog)
    {
        if (battleEvent.Kind is not BattleEventKind.ReenactCardEffects
                and not BattleEventKind.ContingencyTriggered
                and not BattleEventKind.ContingencyResolved
                and not BattleEventKind.OpeningAction
            || battleEvent.ReenactedCard is not { } snapshot)
            return null;

        var card = SnapshotReenactedCard(snapshot, catalog);
        // 先制行動として使ったカードは、説明の一行目にキーワード「先制行動」を付ける。
        return battleEvent.Kind == BattleEventKind.OpeningAction
            ? card with { Description = string.IsNullOrEmpty(card.Description) ? "[K:opening]" : $"[K:opening]\n{card.Description}" }
            : card;
    }

    private static BattleCardDecisionSnapshot SnapshotReenactedCard(
        PlayedCardEffectSnapshot snapshot,
        DataCatalog catalog)
    {
        if (!catalog.TryGetCard(snapshot.CardDefinitionId, out var definition))
        {
            return new BattleCardDecisionSnapshot(
                snapshot.CardInstanceId,
                snapshot.CardDefinitionId,
                snapshot.CardDefinitionId,
                snapshot.CardType.ToString(),
                "Unknown",
                snapshot.IsUpgraded,
                snapshot.OriginalCost,
                snapshot.OriginalCost,
                0,
                string.Empty,
                PoolClass: snapshot.EffectivePoolClass(definition: null).ToString());
        }

        return new BattleCardDecisionSnapshot(
            CardInstanceId: snapshot.CardInstanceId,
            DefinitionId: snapshot.CardDefinitionId,
            DisplayName: definition.DisplayName ?? definition.Name,
            CardType: snapshot.CardType.ToString(),
            Rarity: definition.Rarity.ToString(),
            IsUpgraded: snapshot.IsUpgraded,
            OriginalCost: snapshot.OriginalCost,
            CurrentCost: snapshot.OriginalCost,
            PaidCost: 0,
            Description: CardTextFormatter.Format(
                definition, upgraded: snapshot.IsUpgraded),
            PoolClass: snapshot.EffectivePoolClass(definition).ToString());
    }

    private static IReadOnlyList<BattleActionIntentSnapshot> SnapshotActorIntentDelta(
        BattleState before,
        BattleState after,
        string actorInstanceId)
    {
        var actorBefore = before.Allies.FirstOrDefault(candidate => string.Equals(
            candidate.InstanceId, actorInstanceId, StringComparison.Ordinal));
        var actorAfter = after.Allies.FirstOrDefault(candidate => string.Equals(
            candidate.InstanceId, actorInstanceId, StringComparison.Ordinal));
        if (actorAfter is null) return [];

        var intents = new List<BattleActionIntentSnapshot>();
        AddAttack(
            AddedHits(actorBefore?.AttackSingle ?? AttackPool.Empty,
                actorAfter.AttackSingle),
            "single");
        AddAttack(
            AddedHits(actorBefore?.AttackRandom ?? AttackPool.Empty,
                actorAfter.AttackRandom),
            "random");
        AddAttack(
            AddedHits(actorBefore?.AttackAll ?? AttackPool.Empty,
                actorAfter.AttackAll),
            "all");
        int blockSum = actorAfter.Block.Sum - (actorBefore?.Block.Sum ?? 0);
        int blockAdds = actorAfter.Block.AddCount - (actorBefore?.Block.AddCount ?? 0);
        if (blockSum > 0 || blockAdds > 0)
            intents.Add(new BattleActionIntentSnapshot(
                "block", "self", Math.Max(0, blockSum), Math.Max(1, blockAdds),
                ParticipantIdForActor(after, actorAfter.InstanceId),
                actorAfter.InstanceId));
        return intents;

        void AddAttack(ImmutableArray<AttackHit> hits, string scope)
        {
            if (hits.IsDefaultOrEmpty) return;
            string? targetActorId = scope == "single"
                ? CommonTargetActorId(hits)
                : null;
            intents.Add(new BattleActionIntentSnapshot(
                "attack", scope, hits.Sum(hit => hit.Amount), hits.Length,
                ParticipantIdForActor(after, targetActorId), targetActorId));
        }
    }

    private static IReadOnlyList<BattleActionIntentSnapshot> SnapshotCommittedIntents(
        CpuCommittedAction action,
        BattleState identityState)
    {
        var intents = new List<BattleActionIntentSnapshot>();
        AddAttack(action.IntentDelta.AddedSingleHits, "single");
        AddAttack(action.IntentDelta.AddedRandomHits, "random");
        AddAttack(action.IntentDelta.AddedAllHits, "all");
        if (action.IntentDelta.BlockSumDelta > 0
            || action.IntentDelta.BlockAddCountDelta > 0)
        {
            intents.Add(new BattleActionIntentSnapshot(
                "block",
                "self",
                Math.Max(0, action.IntentDelta.BlockSumDelta),
                Math.Max(1, action.IntentDelta.BlockAddCountDelta),
                action.ParticipantId,
                action.ActorInstanceId));
        }
        return intents;

        void AddAttack(ImmutableArray<AttackHit> hits, string scope)
        {
            if (hits.IsDefaultOrEmpty) return;
            string? targetActorId = scope == "single"
                ? CommonTargetActorId(hits)
                : null;
            intents.Add(new BattleActionIntentSnapshot(
                "attack",
                scope,
                hits.Sum(hit => hit.Amount),
                hits.Length,
                ParticipantIdForActor(identityState, targetActorId),
                targetActorId));
        }
    }

    private static ImmutableArray<AttackHit> AddedHits(
        AttackPool before,
        AttackPool after)
    {
        int shared = Math.Min(before.Hits.Length, after.Hits.Length);
        int prefixLength = 0;
        while (prefixLength < shared
               && Equals(before.Hits[prefixLength], after.Hits[prefixLength]))
        {
            prefixLength++;
        }
        return after.Hits.Skip(prefixLength).ToImmutableArray();
    }

    private static string? CommonTargetActorId(ImmutableArray<AttackHit> hits)
    {
        string? common = null;
        foreach (var hit in hits)
        {
            if (string.IsNullOrWhiteSpace(hit.TargetInstanceId))
                continue;
            if (common is null)
            {
                common = hit.TargetInstanceId;
                continue;
            }
            if (!string.Equals(common, hit.TargetInstanceId, StringComparison.Ordinal))
                return null;
        }
        return common;
    }

    private static CardActorContext ActorContext(
        BattleState state,
        string actorInstanceId)
    {
        var actor = state.Allies.FirstOrDefault(candidate => string.Equals(
            candidate.InstanceId, actorInstanceId, StringComparison.Ordinal));
        return actor is null
            ? CardActorContext.Empty
            : new CardActorContext(
                actor.GetStatus("strength"),
                actor.GetStatus("weak"),
                actor.GetStatus("dexterity"))
            {
                AttackPower = actor.AttackPower,
                DefensePower = actor.DefensePower,
            };
    }

    private static BattleParticipantState GetParticipant(
        BattleState state,
        string participantId) =>
        BattleParticipantAccess.TryGet(state, participantId, out var participant)
            ? participant
            : throw new InvalidOperationException(
                $"Battle participant '{participantId}' does not exist.");

    private static IReadOnlyList<string> ResolveTargets(
        BattleState state,
        BattleParticipantState participant) => ResolveTargets(
            state, participant.TargetEnemyIndex, participant.TargetAllyIndex);

    private static IReadOnlyList<string> ResolveCardTargets(
        BattleState state,
        BattleParticipantState participant,
        BattleCardInstance card,
        DataCatalog catalog)
    {
        if (!catalog.TryGetCard(card.CardDefinitionId, out var definition))
            return ResolveTargets(state, participant);

        var effects = card.IsUpgraded && definition.UpgradedEffects is not null
            ? definition.UpgradedEffects
            : definition.Effects;
        return ResolveEffectTargets(state, participant, effects);
    }

    private static IReadOnlyList<string> ResolveEffectTargets(
        BattleState state,
        BattleParticipantState participant,
        IReadOnlyList<CardEffect> effects)
    {
        bool targetsEnemy = effects.Any(effect =>
            effect.Scope == EffectScope.Single
            && effect.Side == EffectSide.Enemy);
        bool targetsAlly = effects.Any(effect =>
            effect.Scope == EffectScope.Single
            && effect.Side == EffectSide.Ally);
        return ResolveTargets(
            state,
            targetsEnemy ? participant.TargetEnemyIndex : null,
            targetsAlly ? participant.TargetAllyIndex : null);
    }

    private static IReadOnlyList<string> ResolveTargets(
        BattleState state,
        int? enemyIndex,
        int? allyIndex)
    {
        var ids = new List<string>(2);
        if (enemyIndex is >= 0 && enemyIndex < state.Enemies.Length)
            ids.Add(state.Enemies[enemyIndex.Value].InstanceId);
        if (allyIndex is >= 0 && allyIndex < state.Allies.Length)
            ids.Add(state.Allies[allyIndex.Value].InstanceId);
        return ids;
    }

    private static IReadOnlyList<string> AudienceForAction(
        BattleState state,
        string? sourceParticipantId,
        IReadOnlyList<string> targetActorInstanceIds,
        IReadOnlyList<BattleActionEffectSnapshot> effects,
        IReadOnlyList<BattleActionIntentSnapshot> intents) =>
        targetActorInstanceIds
            .Select(actorId => ParticipantIdForActor(state, actorId))
            .Concat(effects.Select(effect => effect.TargetParticipantId))
            .Concat(intents.Select(intent => intent.TargetParticipantId))
            .Where(id => !string.IsNullOrWhiteSpace(id)
                && !string.Equals(id, sourceParticipantId, StringComparison.Ordinal))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();

    private static IReadOnlyList<string> AllParticipantIds(BattleState state) =>
        BattleParticipantAccess.EffectiveParticipants(state)
            .Select(participant => participant.ParticipantId)
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();

    private static string? ParticipantIdForActor(
        BattleState state,
        string? actorInstanceId) =>
        actorInstanceId is not null
        && BattleParticipantAccess.TryGetByActor(
            state, actorInstanceId, out var participant)
            ? participant.ParticipantId
            : null;
}
