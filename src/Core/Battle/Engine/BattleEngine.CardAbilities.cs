using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

public static partial class BattleEngine
{
    private static PlayedCardEffectSnapshot CreatePlayedCardSnapshot(
        BattleState state,
        BattleCardInstance card,
        CardDefinition definition,
        CombatActor actor,
        IReadOnlyList<CardEffect> effects,
        int originalCost,
        int paidCost)
    {
        long sequence = state.NextCardPlaySequence >= long.MaxValue
            ? long.MaxValue
            : Math.Max(0L, state.NextCardPlaySequence) + 1L;
        return new PlayedCardEffectSnapshot(
            definition.Id,
            card.InstanceId,
            actor.InstanceId,
            definition.CardType,
            originalCost,
            paidCost,
            card.IsUpgraded,
            (definition.EffectiveKeywords(card.IsUpgraded)
                ?? Array.Empty<string>()).ToImmutableArray(),
            effects.ToImmutableArray(),
            sequence)
        {
            PoolClass = card.EffectivePoolClass(definition),
        };
    }

    private static BattleState RecordActualCardPlay(
        BattleState state,
        PlayedCardEffectSnapshot snapshot,
        bool isContinuing)
    {
        var comboHistory = isContinuing
            ? state.CurrentComboCardHistory.Add(snapshot)
            : ImmutableArray.Create(snapshot);
        return state with
        {
            PlayedCardHistoryThisTurn = state.PlayedCardHistoryThisTurn.Add(snapshot),
            CurrentComboCardHistory = comboHistory,
            NextCardPlaySequence = snapshot.PlaySequence,
        };
    }

    /// <summary>
    /// Adds one fully resolved real Attack/Skill to the participant's
    /// battle-long Reprise history.  Effect-only executions never call this
    /// helper.  Contingency calls it only after its delayed effect list (and
    /// any choice in that list) has completed.
    /// </summary>
    internal static BattleState RecordRepriseEligibleResolution(
        BattleState state,
        PlayedCardEffectSnapshot snapshot)
    {
        if (snapshot.CardType is not (CardType.Attack or CardType.Skill))
            return state;

        long sequence = state.NextEffectResolutionSequence >= long.MaxValue
            ? long.MaxValue
            : Math.Max(0L, state.NextEffectResolutionSequence) + 1L;
        var completed = snapshot with { ResolutionSequence = sequence };
        return state with
        {
            RepriseEligibleHistory = state.RepriseEligibleHistory.Add(completed),
            NextEffectResolutionSequence = sequence,
            // 全クラス共通: 解決を終えた最後のアタックカードを残光用に保存する
            // （残光は終撃なしでも「このターン最後の攻撃」を再実行できる）。
            // 終撃はこの後でコンボ中の全アタックカードに上書きする。
            LastFinisherRecord = snapshot.CardType == CardType.Attack
                ? ImmutableArray.Create(snapshot)
                : state.LastFinisherRecord,
        };
    }

    private static BattleState RecordActualCardResolutionForReprise(
        BattleState state,
        string cardInstanceId)
    {
        var snapshot = state.PlayedCardHistoryThisTurn
            .LastOrDefault(candidate => string.Equals(
                candidate.CardInstanceId,
                cardInstanceId,
                StringComparison.Ordinal));
        return snapshot is null
            ? state
            : RecordRepriseEligibleResolution(state, snapshot);
    }

    private static TAbility? FindAbility<TAbility>(
        CardDefinition definition,
        bool upgraded)
        where TAbility : CardAbilityDefinition =>
        definition.EffectiveAbilities(upgraded).OfType<TAbility>().FirstOrDefault();

    private static BattleState ArmContingency(
        BattleState state,
        string casterActorInstanceId,
        BattleCardInstance card,
        ContingencyAbilityDefinition ability)
    {
        long latestOrdinal = Math.Max(
            Math.Max(0L, state.NextContingencyOrdinal),
            state.Participants.IsDefaultOrEmpty
                ? 0L
                : state.Participants.Max(participant =>
                    Math.Max(0L, participant.NextContingencyOrdinal)));
        long ordinal = latestOrdinal >= long.MaxValue
            ? long.MaxValue
            : latestOrdinal + 1L;
        return state with
        {
            ArmedContingencies = state.ArmedContingencies.Add(
                new ArmedContingency(
                    card.InstanceId,
                    casterActorInstanceId,
                    ability.Trigger,
                    ordinal)),
            NextContingencyOrdinal = ordinal,
        };
    }

    /// <summary>
    /// An armed Contingency is valid only while the same card instance remains
    /// in the normal exhaust pile. Leaving and later returning does not re-arm it.
    /// </summary>
    internal static BattleState PruneArmedContingencies(BattleState state)
    {
        if (state.ArmedContingencies.IsDefaultOrEmpty)
            return state;
        var exhaustIds = state.ExhaustPile
            .Select(card => card.InstanceId)
            .ToHashSet(StringComparer.Ordinal);
        var kept = state.ArmedContingencies
            .Where(armed => exhaustIds.Contains(armed.CardInstanceId))
            .ToImmutableArray();
        return kept.Length == state.ArmedContingencies.Length
            ? state
            : state with { ArmedContingencies = kept };
    }

    private static BattleState ApplyFinisher(
        BattleState state,
        string casterActorInstanceId,
        BattleCardInstance card,
        CardDefinition definition,
        List<BattleEvent> events,
        ref int order)
    {
        if (FindAbility<FinisherAbilityDefinition>(definition, card.IsUpgraded) is null)
            return state;

        var saved = state.CurrentComboCardHistory
            .Where(snapshot => snapshot.CardType == CardType.Attack)
            .ToImmutableArray();
        int consumedCombo = Math.Max(0, state.ComboCount);
        events.Add(new BattleEvent(
            BattleEventKind.FinisherResolved,
            Order: order++,
            CasterInstanceId: casterActorInstanceId,
            Amount: consumedCombo,
            CardId: definition.Id,
            Note: $"savedAttacks={saved.Length}"));

        return state with
        {
            LastFinisherRecord = saved,
            CurrentComboCardHistory = ImmutableArray<PlayedCardEffectSnapshot>.Empty,
            ComboCount = 0,
            LastPlayedOrigCost = null,
            NextCardComboFreePass = false,
            WildUsedInCurrentCombo = false,
        };
    }

    private static bool MatchesRepriseFilter(
        PlayedCardEffectSnapshot snapshot,
        RepriseCardFilter filter)
    {
        // Reprise is an effect-copy mechanic for ordinary Attack/Skill cards.
        // Power and Unit lifecycle effects must never be copied even if an
        // author omits cardTypes from the filter.
        if (snapshot.CardType is not (CardType.Attack or CardType.Skill))
            return false;
        if (filter.EffectiveCardTypes.Count > 0
            && !filter.EffectiveCardTypes.Contains(snapshot.CardType))
            return false;
        if (filter.MinOriginalCost is { } minOriginal
            && snapshot.OriginalCost < minOriginal)
            return false;
        if (filter.MaxOriginalCost is { } maxOriginal
            && snapshot.OriginalCost > maxOriginal)
            return false;
        if (filter.MinPaidCost is { } minPaid && snapshot.PaidCost < minPaid)
            return false;
        if (filter.MaxPaidCost is { } maxPaid && snapshot.PaidCost > maxPaid)
            return false;
        if (filter.EffectiveRequiredKeywords.Count > 0
            && filter.EffectiveRequiredKeywords.Any(required =>
                !snapshot.Keywords.Contains(required, StringComparer.Ordinal)))
            return false;
        return true;
    }

    private static ContingencyResolutionEngine.Resolution
        ResolveComboContingenciesFromProjectedOwner(
            BattleState state,
            string actorInstanceId,
            int previousCombo,
            int currentCombo,
            IRng rng,
            DataCatalog catalog,
            int orderStart)
    {
        if (state.Participants.IsDefaultOrEmpty)
        {
            return ContingencyResolutionEngine.ResolveComboReached(
                state,
                actorInstanceId,
                previousCombo,
                currentCombo,
                rng,
                catalog,
                orderStart);
        }

        if (!BattleParticipantAccess.TryGetByActor(
                state, actorInstanceId, out var participant))
            return new ContingencyResolutionEngine.Resolution(
                state,
                Array.Empty<BattleEvent>(),
                PausedForChoice: false);

        var globallySynced = BattleParticipantAccess.CommitRootToParticipant(
            state, participant.ParticipantId);
        var resolved = ContingencyResolutionEngine.ResolveComboReached(
            globallySynced,
            actorInstanceId,
            previousCombo,
            currentCombo,
            rng,
            catalog,
            orderStart);
        return resolved with
        {
            State = BattleParticipantAccess.ProjectToRoot(
                resolved.State, participant.ParticipantId),
        };
    }

    private static (
        BattleState state,
        List<BattleEvent> events,
        bool paused) ApplyEffectSnapshotFrom(
        BattleState state,
        CombatActor caster,
        PlayedCardEffectSnapshot snapshot,
        int startIndex,
        string playingCardInstanceId,
        ImmutableArray<string> summonedActorInstanceIdsBefore,
        EffectExecutionKind kind,
        string causeDefinitionId,
        ref int order,
        IRng rng,
        DataCatalog catalog)
    {
        var s = state;
        var events = new List<BattleEvent>();
        catalog.TryGetCard(snapshot.CardDefinitionId, out var snapshotDefinition);
        var snapshotPoolClass = snapshot.EffectivePoolClass(snapshotDefinition);
        string? excludedHandCardInstanceId = kind == EffectExecutionKind.Reprise
            ? playingCardInstanceId
            : null;
        for (int effectIndex = startIndex;
             effectIndex < snapshot.Effects.Length;
             effectIndex++)
        {
            var effect = snapshot.Effects[effectIndex];
            if (!string.IsNullOrEmpty(effect.Trigger))
                continue;
            // These legacy actions describe the physical lifecycle of the
            // source card. Effect-only execution never moves or exhausts that
            // card and must not emit phantom exhaust/discard triggers.
            if (effect.Action is "exhaustSelf" or "retainSelf")
                continue;
            if (effect.ComboMin is { } comboMin && s.ComboCount < comboMin)
                continue;
            if (effect.MaxComboMin is { } maxComboMin
                && s.MaxComboThisTurn < maxComboMin)
                continue;

            if (EffectApplier.NeedsPlayerChoice(
                    s, effect, catalog, excludedHandCardInstanceId))
            {
                var choice = EffectApplier.BuildPendingChoice(
                    s, effect, catalog, excludedHandCardInstanceId);
                if (BattleParticipantAccess.TryGetByActor(
                        s, caster.InstanceId, out var owner)
                    && owner.ControllerKind == BattleParticipantControllerKind.Cpu)
                {
                    var cpuSource = new CombatSource(
                        CombatSourceKind.CardEffect,
                        OwnerSlot: caster.OwnerPlayerSlot ?? caster.SlotIndex,
                        OwnerInstanceId: caster.OwnerActorInstanceId ?? caster.InstanceId,
                        ActorInstanceId: caster.InstanceId,
                        DefinitionId: snapshot.CardDefinitionId,
                        InstanceId: snapshot.CardInstanceId,
                        EffectIndex: effectIndex,
                        Scope: effect.Scope.ToString().ToLowerInvariant());
                    var selected = choice.CandidateInstanceIds
                        .Take(choice.Count)
                        .ToImmutableArray();
                    var (afterChoice, choiceEvents) =
                        EffectApplier.ApplyChoseEffect(
                            s,
                            caster,
                            effect,
                            selected,
                            rng,
                            catalog,
                            cpuSource,
                            playingCardInstanceId: excludedHandCardInstanceId);
                    s = PruneArmedContingencies(afterChoice);
                    foreach (var battleEvent in choiceEvents)
                        events.Add(battleEvent with { Order = order++ });
                    caster = FindParticipantCaster(s, caster.InstanceId) ?? caster;
                    continue;
                }

                var pending = new PendingCardPlay(
                    playingCardInstanceId,
                    effectIndex,
                    summonedActorInstanceIdsBefore,
                    choice)
                {
                    EffectExecution = new PendingEffectExecution(
                        kind,
                        snapshot,
                        caster.InstanceId,
                        causeDefinitionId),
                };
                return (
                    s with { PendingCardPlay = pending },
                    events,
                    true);
            }

            var source = new CombatSource(
                CombatSourceKind.CardEffect,
                OwnerSlot: caster.OwnerPlayerSlot ?? caster.SlotIndex,
                OwnerInstanceId: caster.OwnerActorInstanceId ?? caster.InstanceId,
                ActorInstanceId: caster.InstanceId,
                DefinitionId: snapshot.CardDefinitionId,
                InstanceId: snapshot.CardInstanceId,
                EffectIndex: effectIndex,
                Scope: effect.Scope.ToString().ToLowerInvariant());
            var (afterEffect, effectEvents) = EffectApplier.Apply(
                s,
                caster,
                effect,
                rng,
                catalog,
                source,
                playingCardInstanceId: excludedHandCardInstanceId,
                sourcePoolClass: snapshotPoolClass);
            s = PruneArmedContingencies(afterEffect);
            foreach (var battleEvent in effectEvents)
                events.Add(battleEvent with { Order = order++ });
            caster = FindParticipantCaster(s, caster.InstanceId) ?? caster;
        }

        return (s, events, false);
    }

    internal static (
        BattleState State,
        IReadOnlyList<BattleEvent> Events,
        bool Paused) ResolveAfterimageAtActorEnd(
        BattleState state,
        string actorInstanceId,
        IRng rng,
        DataCatalog catalog,
        int orderStart)
    {
        if (BattleOutcomeResolver.Evaluate(state)
            != RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending)
        {
            return (state, Array.Empty<BattleEvent>(), false);
        }

        int actorIndex = -1;
        for (int index = 0; index < state.Allies.Length; index++)
        {
            if (!string.Equals(
                    state.Allies[index].InstanceId,
                    actorInstanceId,
                    StringComparison.Ordinal))
                continue;
            actorIndex = index;
            break;
        }
        if (actorIndex < 0 || !state.Allies[actorIndex].IsAlive)
            return (state, Array.Empty<BattleEvent>(), false);
        int afterimage = state.Allies[actorIndex].GetStatus(
            RoguelikeCardGame.Core.Battle.Statuses.BattleStatusIds.Afterimage);
        if (afterimage <= 0)
            return (state, Array.Empty<BattleEvent>(), false);

        var consumedActor = state.Allies[actorIndex] with
        {
            Statuses = state.Allies[actorIndex].Statuses.Remove(
                RoguelikeCardGame.Core.Battle.Statuses.BattleStatusIds.Afterimage),
            StatusSources = state.Allies[actorIndex].StatusSources.Remove(
                RoguelikeCardGame.Core.Battle.Statuses.BattleStatusIds.Afterimage),
        };
        var s = state with
        {
            Allies = state.Allies.SetItem(actorIndex, consumedActor),
        };
        var events = new List<BattleEvent>();
        int order = orderStart;
        events.Add(new BattleEvent(
            BattleEventKind.RemoveStatus,
            Order: order++,
            TargetInstanceId: actorInstanceId,
            Note: RoguelikeCardGame.Core.Battle.Statuses.BattleStatusIds.Afterimage));

        // Actors without card resources cannot own a Finisher record, but a
        // Residual/Afterimage buff granted to them still resolves as an empty
        // replay and must be consumed at their attack-slot boundary.
        if (!BattleParticipantAccess.TryGetByActor(
                state, actorInstanceId, out var participant))
            return (s, events, false);

        if (participant.LastFinisherRecord.IsDefaultOrEmpty)
            return (s, events, false);

        var sequence = ImmutableArray.CreateBuilder<PlayedCardEffectSnapshot>(
            participant.LastFinisherRecord.Length * afterimage);
        for (int cycle = 0; cycle < afterimage; cycle++)
            sequence.AddRange(participant.LastFinisherRecord);

        for (int index = 0; index < sequence.Count; index++)
        {
            if (BattleOutcomeResolver.Evaluate(s)
                != RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending)
                break;

            var sourceCard = sequence[index];
            if (!BattleParticipantAccess.TryGetByActor(
                    s, actorInstanceId, out var currentParticipant))
                break;
            var projected = s.Participants.IsDefaultOrEmpty
                ? s
                : BattleParticipantAccess.ProjectToRoot(
                    s, currentParticipant.ParticipantId);
            var caster = FindParticipantCaster(projected, actorInstanceId);
            if (caster is null || !caster.IsAlive)
                break;

            int beforeSingle = caster.AttackSingle.AddCount;
            int beforeRandom = caster.AttackRandom.AddCount;
            int beforeAll = caster.AttackAll.AddCount;
            events.Add(new BattleEvent(
                BattleEventKind.ReenactCardEffects,
                Order: order++,
                CasterInstanceId: actorInstanceId,
                CardId: sourceCard.CardDefinitionId,
                Note: $"afterimage:{index + 1}/{sequence.Count}",
                Source: CreateReenactSource(caster, sourceCard),
                ReenactedCard: sourceCard));

            var (afterEffects, effectEvents, paused) = ApplyEffectSnapshotFrom(
                projected,
                caster,
                sourceCard,
                startIndex: 0,
                sourceCard.CardInstanceId,
                ImmutableArray<string>.Empty,
                EffectExecutionKind.Afterimage,
                RoguelikeCardGame.Core.Battle.Statuses.BattleStatusIds.Afterimage,
                ref order,
                rng,
                catalog);
            events.AddRange(effectEvents);
            if (paused)
            {
                var pending = afterEffects.PendingCardPlay!;
                var continuation = pending.EffectExecution! with
                {
                    RemainingCards = sequence
                        .Skip(index + 1)
                        .ToImmutableArray(),
                    BeforeSingleAttackCount = beforeSingle,
                    BeforeRandomAttackCount = beforeRandom,
                    BeforeAllAttackCount = beforeAll,
                };
                afterEffects = afterEffects with
                {
                    PendingCardPlay = pending with
                    {
                        EffectExecution = continuation,
                    },
                };
                s = s.Participants.IsDefaultOrEmpty
                    ? afterEffects
                    : BattleParticipantAccess.CommitRootToParticipant(
                        afterEffects, currentParticipant.ParticipantId);
                return (s, events, true);
            }

            s = s.Participants.IsDefaultOrEmpty
                ? afterEffects
                : BattleParticipantAccess.CommitRootToParticipant(
                    afterEffects, currentParticipant.ParticipantId);
            var (afterAttacks, attackEvents) =
                PlayerAttackingResolver.ResolveAddedAttacks(
                    s,
                    actorInstanceId,
                    beforeSingle,
                    beforeRandom,
                    beforeAll,
                    rng,
                    catalog,
                    order);
            s = afterAttacks;
            foreach (var battleEvent in attackEvents)
                events.Add(battleEvent with { Order = order++ });

            if (BattleOutcomeResolver.Evaluate(s)
                != RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending)
                break;
        }

        return (s, events, false);
    }

    private static (BattleState state, IReadOnlyList<BattleEvent> events)
        ResolveAfterimageChoice(
            BattleState state,
            PendingCardPlay pending,
            ImmutableArray<string> selectedInstanceIds,
            IRng rng,
            DataCatalog catalog)
    {
        var continuation = pending.EffectExecution
            ?? throw new InvalidOperationException(
                "Afterimage choice continuation is missing.");
        var caster = FindParticipantCaster(
            state, continuation.CasterActorInstanceId)
            ?? throw new InvalidOperationException(
                $"Afterimage caster '{continuation.CasterActorInstanceId}' is unavailable.");
        if (pending.EffectIndex < 0
            || pending.EffectIndex >= continuation.SourceCard.Effects.Length)
            throw new InvalidOperationException(
                "Pending Afterimage choice index is out of range.");

        var effect = continuation.SourceCard.Effects[pending.EffectIndex];
        var source = new CombatSource(
            CombatSourceKind.CardEffect,
            OwnerSlot: caster.OwnerPlayerSlot ?? caster.SlotIndex,
            OwnerInstanceId: caster.OwnerActorInstanceId ?? caster.InstanceId,
            ActorInstanceId: caster.InstanceId,
            DefinitionId: continuation.SourceCard.CardDefinitionId,
            InstanceId: continuation.SourceCard.CardInstanceId,
            EffectIndex: pending.EffectIndex,
            Scope: effect.Scope.ToString().ToLowerInvariant());
        var s = state with { PendingCardPlay = null };
        var events = new List<BattleEvent>();
        int order = 0;
        var (afterChoice, choiceEvents) = EffectApplier.ApplyChoseEffect(
            s,
            caster,
            effect,
            selectedInstanceIds,
            rng,
            catalog,
            source,
            playingCardInstanceId: null);
        s = PruneArmedContingencies(afterChoice);
        foreach (var battleEvent in choiceEvents)
            events.Add(battleEvent with { Order = order++ });

        if (BattleOutcomeResolver.Evaluate(s)
            != RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending)
            return (s, events);

        caster = FindParticipantCaster(s, caster.InstanceId) ?? caster;
        var (afterRemainingEffects, remainingEffectEvents, paused) =
            ApplyEffectSnapshotFrom(
                s,
                caster,
                continuation.SourceCard,
                pending.EffectIndex + 1,
                continuation.SourceCard.CardInstanceId,
                ImmutableArray<string>.Empty,
                EffectExecutionKind.Afterimage,
                continuation.CauseDefinitionId,
                ref order,
                rng,
                catalog);
        s = afterRemainingEffects;
        events.AddRange(remainingEffectEvents);
        if (paused)
        {
            var nextPending = s.PendingCardPlay!;
            s = s with
            {
                PendingCardPlay = nextPending with
                {
                    EffectExecution = nextPending.EffectExecution! with
                    {
                        RemainingCards = continuation.RemainingCards,
                        BeforeSingleAttackCount = continuation.BeforeSingleAttackCount,
                        BeforeRandomAttackCount = continuation.BeforeRandomAttackCount,
                        BeforeAllAttackCount = continuation.BeforeAllAttackCount,
                    },
                },
            };
            return (s, events);
        }

        var (afterCurrentAttacks, currentAttackEvents) =
            PlayerAttackingResolver.ResolveAddedAttacks(
                s,
                caster.InstanceId,
                continuation.BeforeSingleAttackCount,
                continuation.BeforeRandomAttackCount,
                continuation.BeforeAllAttackCount,
                rng,
                catalog,
                order);
        s = afterCurrentAttacks;
        foreach (var battleEvent in currentAttackEvents)
            events.Add(battleEvent with { Order = order++ });

        if (BattleOutcomeResolver.Evaluate(s)
            != RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending)
            return (s, events);

        for (int index = 0; index < continuation.RemainingCards.Length; index++)
        {
            if (BattleOutcomeResolver.Evaluate(s)
                != RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending)
                break;

            caster = FindParticipantCaster(s, caster.InstanceId) ?? caster;
            if (!caster.IsAlive)
                break;
            var sourceCard = continuation.RemainingCards[index];
            int beforeSingle = caster.AttackSingle.AddCount;
            int beforeRandom = caster.AttackRandom.AddCount;
            int beforeAll = caster.AttackAll.AddCount;
            events.Add(new BattleEvent(
                BattleEventKind.ReenactCardEffects,
                Order: order++,
                CasterInstanceId: caster.InstanceId,
                CardId: sourceCard.CardDefinitionId,
                Note: "afterimage:continued",
                Source: CreateReenactSource(caster, sourceCard),
                ReenactedCard: sourceCard));
            var (afterEffects, effectEvents, nextPaused) = ApplyEffectSnapshotFrom(
                s,
                caster,
                sourceCard,
                0,
                sourceCard.CardInstanceId,
                ImmutableArray<string>.Empty,
                EffectExecutionKind.Afterimage,
                continuation.CauseDefinitionId,
                ref order,
                rng,
                catalog);
            s = afterEffects;
            events.AddRange(effectEvents);
            if (nextPaused)
            {
                var nextPending = s.PendingCardPlay!;
                s = s with
                {
                    PendingCardPlay = nextPending with
                    {
                        EffectExecution = nextPending.EffectExecution! with
                        {
                            RemainingCards = continuation.RemainingCards
                                .Skip(index + 1)
                                .ToImmutableArray(),
                            BeforeSingleAttackCount = beforeSingle,
                            BeforeRandomAttackCount = beforeRandom,
                            BeforeAllAttackCount = beforeAll,
                        },
                    },
                };
                return (s, events);
            }

            var (afterAttacks, attackEvents) =
                PlayerAttackingResolver.ResolveAddedAttacks(
                    s,
                    caster.InstanceId,
                    beforeSingle,
                    beforeRandom,
                    beforeAll,
                    rng,
                    catalog,
                    order);
            s = afterAttacks;
            foreach (var battleEvent in attackEvents)
                events.Add(battleEvent with { Order = order++ });

            if (BattleOutcomeResolver.Evaluate(s)
                != RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending)
                break;
        }

        return (s, events);
    }

    private static (BattleState state, IReadOnlyList<BattleEvent> events)
        ResolveContingencyChoice(
            BattleState state,
            PendingCardPlay pending,
            ImmutableArray<string> selectedInstanceIds,
            IRng rng,
            DataCatalog catalog)
    {
        var continuation = pending.EffectExecution
            ?? throw new InvalidOperationException(
                "Contingency choice continuation is missing.");
        var caster = FindParticipantCaster(
            state, continuation.CasterActorInstanceId)
            ?? throw new InvalidOperationException(
                $"Contingency caster '{continuation.CasterActorInstanceId}' is unavailable.");
        if (pending.EffectIndex < 0
            || pending.EffectIndex >= continuation.SourceCard.Effects.Length)
            throw new InvalidOperationException(
                "Pending Contingency choice index is out of range.");

        var effect = continuation.SourceCard.Effects[pending.EffectIndex];
        var source = new CombatSource(
            CombatSourceKind.CardEffect,
            OwnerSlot: caster.OwnerPlayerSlot ?? caster.SlotIndex,
            OwnerInstanceId: caster.OwnerActorInstanceId ?? caster.InstanceId,
            ActorInstanceId: caster.InstanceId,
            DefinitionId: continuation.SourceCard.CardDefinitionId,
            InstanceId: continuation.SourceCard.CardInstanceId,
            EffectIndex: pending.EffectIndex,
            Scope: effect.Scope.ToString().ToLowerInvariant());
        var s = state with { PendingCardPlay = null };
        var events = new List<BattleEvent>();
        int order = 0;
        var (afterChoice, choiceEvents) = EffectApplier.ApplyChoseEffect(
            s,
            caster,
            effect,
            selectedInstanceIds,
            rng,
            catalog,
            source,
            playingCardInstanceId: null);
        s = PruneArmedContingencies(afterChoice);
        foreach (var battleEvent in choiceEvents)
            events.Add(battleEvent with { Order = order++ });

        caster = FindParticipantCaster(s, caster.InstanceId) ?? caster;
        var (afterRemaining, remainingEvents, paused) = ApplyEffectSnapshotFrom(
            s,
            caster,
            continuation.SourceCard,
            pending.EffectIndex + 1,
            continuation.SourceCard.CardInstanceId,
            ImmutableArray<string>.Empty,
            EffectExecutionKind.Contingency,
            continuation.CauseDefinitionId,
            ref order,
            rng,
            catalog);
        s = afterRemaining;
        events.AddRange(remainingEvents);
        if (paused)
        {
            var nextPending = s.PendingCardPlay!;
            s = s with
            {
                PendingCardPlay = nextPending with
                {
                    EffectExecution = nextPending.EffectExecution! with
                    {
                        ArmedContingencyCardInstanceId =
                            continuation.ArmedContingencyCardInstanceId,
                        BeforeSingleAttackCount = continuation.BeforeSingleAttackCount,
                        BeforeRandomAttackCount = continuation.BeforeRandomAttackCount,
                        BeforeAllAttackCount = continuation.BeforeAllAttackCount,
                        TriggerPreviousCombo = continuation.TriggerPreviousCombo,
                        TriggerCurrentCombo = continuation.TriggerCurrentCombo,
                        PostContingencyFinisherCard =
                            continuation.PostContingencyFinisherCard,
                        PostContingencyFinisherActorInstanceId =
                            continuation.PostContingencyFinisherActorInstanceId,
                    },
                },
            };
            return (s, events);
        }

        var completion = ContingencyResolutionEngine.CompleteCardDestination(
            s,
            continuation.ArmedContingencyCardInstanceId
                ?? continuation.SourceCard.CardInstanceId,
            catalog);
        s = completion.State;
        events.Add(new BattleEvent(
            BattleEventKind.ContingencyResolved,
            Order: order++,
            CasterInstanceId: caster.InstanceId,
            CardId: continuation.SourceCard.CardDefinitionId,
            Note: completion.Destination,
            ReenactedCard: continuation.SourceCard));
        s = RecordRepriseEligibleResolution(s, continuation.SourceCard);
        var traitResult = CharacterTraitRuntime.TryTrigger(
            s,
            caster.InstanceId,
            CharacterTraitTrigger.OnContingencyTriggered,
            catalog,
            rng,
            orderStart: order);
        s = traitResult.State;
        foreach (var battleEvent in traitResult.Events)
            events.Add(battleEvent with { Order = order++ });

        if (continuation.CauseDefinitionId.EndsWith(
                ContingencyTriggerTiming.BeforeEnemyAttack.ToString(),
                StringComparison.Ordinal))
        {
            var (afterAttacks, attackEvents) =
                PlayerAttackingResolver.ResolveAddedAttacks(
                    s,
                    caster.InstanceId,
                    continuation.BeforeSingleAttackCount,
                    continuation.BeforeRandomAttackCount,
                    continuation.BeforeAllAttackCount,
                    rng,
                    catalog,
                    order);
            s = afterAttacks;
            foreach (var battleEvent in attackEvents)
                events.Add(battleEvent with { Order = order++ });
        }

        return (s, events);
    }

    private static (
        BattleState state,
        List<BattleEvent> events,
        bool paused) ExecuteRepriseIfPresent(
        BattleState state,
        CombatActor caster,
        BattleCardInstance playingCard,
        CardDefinition playingDefinition,
        ImmutableArray<string> summonedActorInstanceIds,
        ref int order,
        IRng rng,
        DataCatalog catalog)
    {
        var ability = FindAbility<RepriseAbilityDefinition>(
            playingDefinition, playingCard.IsUpgraded);
        if (ability is null)
            return (state, new List<BattleEvent>(), false);

        var sourceCard = state.RepriseEligibleHistory
            .Select((snapshot, historyIndex) => (snapshot, historyIndex))
            .Where(entry => string.Equals(
                entry.snapshot.ActorInstanceId,
                caster.InstanceId,
                StringComparison.Ordinal))
            .OrderByDescending(entry => entry.snapshot.ResolutionSequence)
            // Saturated counters can tie at long.MaxValue. Append position is
            // the deterministic completion-order tiebreaker, never PlaySequence.
            .ThenByDescending(entry => entry.historyIndex)
            .Select(entry => entry.snapshot)
            .FirstOrDefault(snapshot => MatchesRepriseFilter(snapshot, ability.Filter));
        if (sourceCard is null)
            return (state, new List<BattleEvent>(), false);

        var events = new List<BattleEvent>
        {
            new(
                BattleEventKind.ReenactCardEffects,
                Order: order++,
                CasterInstanceId: caster.InstanceId,
                CardId: sourceCard.CardDefinitionId,
                Note: $"reprise:{playingDefinition.Id}",
                Source: CreateReenactSource(caster, sourceCard),
                ReenactedCard: sourceCard),
        };
        var (afterEffects, effectEvents, paused) = ApplyEffectSnapshotFrom(
            state,
            caster,
            sourceCard,
            startIndex: 0,
            playingCard.InstanceId,
            summonedActorInstanceIds,
            EffectExecutionKind.Reprise,
            playingDefinition.Id,
            ref order,
            rng,
            catalog);
        events.AddRange(effectEvents);
        return (afterEffects, events, paused);
    }

    private static CombatSource CreateReenactSource(
        CombatActor caster,
        PlayedCardEffectSnapshot sourceCard) => new(
            CombatSourceKind.CardEffect,
            OwnerSlot: caster.OwnerPlayerSlot ?? caster.SlotIndex,
            OwnerInstanceId: caster.OwnerActorInstanceId ?? caster.InstanceId,
            ActorInstanceId: caster.InstanceId,
            DefinitionId: sourceCard.CardDefinitionId,
            InstanceId: sourceCard.CardInstanceId,
            Scope: "reenact");

    private static (BattleState state, IReadOnlyList<BattleEvent> events)
        ResolveEffectExecutionChoice(
            BattleState state,
            PendingCardPlay pending,
            BattleCardInstance playingCard,
            CardDefinition playingDefinition,
            ImmutableArray<string> selectedInstanceIds,
            IRng rng,
            DataCatalog catalog)
    {
        var continuation = pending.EffectExecution
            ?? throw new InvalidOperationException(
                "Effect-only choice continuation is missing.");
        if (continuation.Kind != EffectExecutionKind.Reprise)
            throw new InvalidOperationException(
                $"Effect execution kind '{continuation.Kind}' is not resumable yet.");
        if (pending.EffectIndex < 0
            || pending.EffectIndex >= continuation.SourceCard.Effects.Length)
            throw new InvalidOperationException(
                "Pending effect-only choice index is out of range.");

        var caster = FindParticipantCaster(
            state, continuation.CasterActorInstanceId)
            ?? throw new InvalidOperationException(
                $"Effect execution caster '{continuation.CasterActorInstanceId}' is unavailable.");
        var effect = continuation.SourceCard.Effects[pending.EffectIndex];
        var source = new CombatSource(
            CombatSourceKind.CardEffect,
            OwnerSlot: caster.OwnerPlayerSlot ?? caster.SlotIndex,
            OwnerInstanceId: caster.OwnerActorInstanceId ?? caster.InstanceId,
            ActorInstanceId: caster.InstanceId,
            DefinitionId: continuation.SourceCard.CardDefinitionId,
            InstanceId: continuation.SourceCard.CardInstanceId,
            EffectIndex: pending.EffectIndex,
            Scope: effect.Scope.ToString().ToLowerInvariant());

        var s = state with { PendingCardPlay = null };
        var events = new List<BattleEvent>();
        int order = 0;
        var (afterChoice, choiceEvents) = EffectApplier.ApplyChoseEffect(
            s,
            caster,
            effect,
            selectedInstanceIds,
            rng,
            catalog,
            source,
            playingCardInstanceId: playingCard.InstanceId);
        s = PruneArmedContingencies(afterChoice);
        foreach (var battleEvent in choiceEvents)
            events.Add(battleEvent with { Order = order++ });

        caster = FindParticipantCaster(s, caster.InstanceId) ?? caster;
        var (afterRemaining, remainingEvents, paused) = ApplyEffectSnapshotFrom(
            s,
            caster,
            continuation.SourceCard,
            pending.EffectIndex + 1,
            playingCard.InstanceId,
            pending.SummonedActorInstanceIdsBefore,
            continuation.Kind,
            continuation.CauseDefinitionId,
            ref order,
            rng,
            catalog);
        s = afterRemaining;
        events.AddRange(remainingEvents);
        if (paused)
            return (s, events);

        return FinalizeCardPlay(
            s,
            caster.InstanceId,
            playingCard,
            playingDefinition,
            pending.SummonedActorInstanceIdsBefore,
            events,
            ref order,
            rng,
            catalog);
    }
}
