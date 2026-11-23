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

/// <summary>
/// Resolves armed Contingency cards. An armed card is still an ordinary card
/// in its owner's exhaust pile; the separate registry only marks that exact
/// pile membership as waiting for a trigger.
/// </summary>
internal static class ContingencyResolutionEngine
{
    internal sealed record Resolution(
        BattleState State,
        IReadOnlyList<BattleEvent> Events,
        bool PausedForChoice);

    public static Resolution ResolveTurnStart(
        BattleState state,
        IRng rng,
        DataCatalog catalog,
        int orderStart = 0) =>
        ResolveMatching(
            state,
            armed => armed.Trigger.Timing == ContingencyTriggerTiming.TurnStart,
            ContingencyTriggerTiming.TurnStart,
            resolveAddedAttacksImmediately: false,
            triggerPreviousCombo: null,
            triggerCurrentCombo: null,
            rng,
            catalog,
            orderStart);

    public static Resolution ResolveBeforeEnemyAttack(
        BattleState state,
        IRng rng,
        DataCatalog catalog,
        int orderStart = 0) =>
        ResolveMatching(
            state,
            armed => armed.Trigger.Timing
                == ContingencyTriggerTiming.BeforeEnemyAttack,
            ContingencyTriggerTiming.BeforeEnemyAttack,
            // PlayerAttacking has already completed. Any Attack effect created
            // here must therefore resolve before the enemy's first hit instead
            // of being erased by the upcoming turn-end cleanup.
            resolveAddedAttacksImmediately: true,
            triggerPreviousCombo: null,
            triggerCurrentCombo: null,
            rng,
            catalog,
            orderStart);

    public static Resolution ResolveComboReached(
        BattleState state,
        string actorInstanceId,
        int previousCombo,
        int currentCombo,
        IRng rng,
        DataCatalog catalog,
        int orderStart = 0)
    {
        if (string.IsNullOrWhiteSpace(actorInstanceId))
            throw new ArgumentException(
                "Actor instance id must not be empty.", nameof(actorInstanceId));

        return ResolveMatching(
            state,
            armed =>
            {
                int threshold = armed.Trigger.ComboThreshold ?? 0;
                return armed.Trigger.Timing
                        == ContingencyTriggerTiming.ComboReached
                    && string.Equals(
                        armed.ActorInstanceId,
                        actorInstanceId,
                        StringComparison.Ordinal)
                    && threshold > 0
                    && previousCombo < threshold
                    && currentCombo >= threshold;
            },
            ContingencyTriggerTiming.ComboReached,
            resolveAddedAttacksImmediately: false,
            triggerPreviousCombo: previousCombo,
            triggerCurrentCombo: currentCombo,
            rng,
            catalog,
            orderStart);
    }

    private static Resolution ResolveMatching(
        BattleState state,
        Func<ArmedContingency, bool> matches,
        ContingencyTriggerTiming timing,
        bool resolveAddedAttacksImmediately,
        int? triggerPreviousCombo,
        int? triggerCurrentCombo,
        IRng rng,
        DataCatalog catalog,
        int orderStart)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(catalog);

        if (HasPendingChoice(state))
            return new Resolution(
                state, ImmutableArray<BattleEvent>.Empty,
                PausedForChoice: true);

        var candidates = SnapshotCandidates(state, matches);
        if (candidates.Count == 0)
            return new Resolution(
                state, ImmutableArray<BattleEvent>.Empty,
                PausedForChoice: false);

        var s = state;
        var events = new List<BattleEvent>();
        int order = orderStart;
        foreach (var candidate in candidates)
        {
            if (candidate.ParticipantId is null)
            {
                var ownerResult = ResolveProjectedCandidate(
                    s,
                    candidate,
                    matches,
                    timing,
                    resolveAddedAttacksImmediately,
                    triggerPreviousCombo,
                    triggerCurrentCombo,
                    autoResolveChoices: false,
                    rng,
                    catalog,
                    events,
                    ref order);
                s = ownerResult.State;
                if (ownerResult.PausedForChoice)
                    return new Resolution(s, events, true);
                continue;
            }

            if (!BattleParticipantAccess.TryGet(
                    s, candidate.ParticipantId, out var participant))
                continue;

            var projected = BattleParticipantAccess.ProjectToRoot(
                s, candidate.ParticipantId);
            var projectedResult = ResolveProjectedCandidate(
                projected,
                candidate,
                matches,
                timing,
                resolveAddedAttacksImmediately,
                triggerPreviousCombo,
                triggerCurrentCombo,
                participant.ControllerKind
                    == BattleParticipantControllerKind.Cpu,
                rng,
                catalog,
                events,
                ref order);
            s = BattleParticipantAccess.CommitRootToParticipant(
                projectedResult.State, candidate.ParticipantId);
            if (projectedResult.PausedForChoice)
                return new Resolution(s, events, true);
        }

        return new Resolution(s, events, PausedForChoice: false);
    }

    private static Resolution ResolveProjectedCandidate(
        BattleState state,
        Candidate candidate,
        Func<ArmedContingency, bool> matches,
        ContingencyTriggerTiming timing,
        bool resolveAddedAttacksImmediately,
        int? triggerPreviousCombo,
        int? triggerCurrentCombo,
        bool autoResolveChoices,
        IRng rng,
        DataCatalog catalog,
        List<BattleEvent> events,
        ref int order)
    {
        var s = BattleEngine.PruneArmedContingencies(state);
        var armed = s.ArmedContingencies.FirstOrDefault(current =>
            current.ArmedOrdinal == candidate.ArmedOrdinal
            && string.Equals(
                current.CardInstanceId,
                candidate.CardInstanceId,
                StringComparison.Ordinal));
        if (armed is null || !matches(armed))
            return new Resolution(s, events, PausedForChoice: false);

        int exhaustIndex = FindCardIndex(
            s.ExhaustPile, armed.CardInstanceId);
        if (exhaustIndex < 0)
        {
            s = RemoveArmedRecord(s, armed);
            return new Resolution(s, events, PausedForChoice: false);
        }

        var caster = s.Allies.FirstOrDefault(actor =>
            actor.IsAlive
            && string.Equals(
                actor.InstanceId,
                armed.ActorInstanceId,
                StringComparison.Ordinal));
        if (caster is null)
            return new Resolution(s, events, PausedForChoice: false);

        var card = s.ExhaustPile[exhaustIndex];
        if (!catalog.TryGetCard(card.CardDefinitionId, out var definition))
            throw new InvalidOperationException(
                $"Armed Contingency card definition '{card.CardDefinitionId}' does not exist.");

        var snapshot = CreateCurrentVersionSnapshot(
            s, armed, card, definition);

        // Disarm at trigger entry. This prevents nested processing from firing
        // the same card twice and also leaves an Exhaust-keyword Contingency in
        // its required post-resolution, non-waiting state. Keep the exact card
        // snapshot on the event: presentation must not try to reconstruct the
        // card after it has moved out of the exhaust pile.
        s = RemoveArmedRecord(s, armed);
        events.Add(new BattleEvent(
            BattleEventKind.ContingencyTriggered,
            Order: order++,
            CasterInstanceId: caster.InstanceId,
            CardId: definition.Id,
            Note: timing.ToString(),
            ReenactedCard: snapshot));
        int beforeSingleCount = caster.AttackSingle.AddCount;
        int beforeRandomCount = caster.AttackRandom.AddCount;
        int beforeAllCount = caster.AttackAll.AddCount;

        for (int effectIndex = 0;
             effectIndex < snapshot.Effects.Length;
             effectIndex++)
        {
            var effect = snapshot.Effects[effectIndex];
            if (!string.IsNullOrEmpty(effect.Trigger))
                continue;
            if (effect.ComboMin is { } comboMin
                && s.ComboCount < comboMin)
                continue;
            if (effect.MaxComboMin is { } maxComboMin
                && s.MaxComboThisTurn < maxComboMin)
                continue;

            if (EffectApplier.NeedsPlayerChoice(
                    s, effect, catalog, card.InstanceId))
            {
                var choice = EffectApplier.BuildPendingChoice(
                    s, effect, catalog, card.InstanceId);
                if (!autoResolveChoices)
                {
                    var pending = new PendingCardPlay(
                        card.InstanceId,
                        effectIndex,
                        ImmutableArray<string>.Empty,
                        choice)
                    {
                        EffectExecution = new PendingEffectExecution(
                            EffectExecutionKind.Contingency,
                            snapshot,
                            caster.InstanceId,
                            $"contingency:{timing}")
                        {
                            ArmedContingencyCardInstanceId = card.InstanceId,
                            BeforeSingleAttackCount = beforeSingleCount,
                            BeforeRandomAttackCount = beforeRandomCount,
                            BeforeAllAttackCount = beforeAllCount,
                            TriggerPreviousCombo = triggerPreviousCombo,
                            TriggerCurrentCombo = triggerCurrentCombo,
                        },
                    };
                    return new Resolution(
                        s with { PendingCardPlay = pending },
                        events,
                        PausedForChoice: true);
                }

                var selected = choice.CandidateInstanceIds
                    .Take(choice.Count)
                    .ToImmutableArray();
                var source = CreateSource(caster, snapshot, effectIndex, effect);
                var (afterChoice, choiceEvents) = EffectApplier.ApplyChoseEffect(
                    s,
                    caster,
                    effect,
                    selected,
                    rng,
                    catalog,
                    source,
                    playingCardInstanceId: card.InstanceId);
                s = BattleEngine.PruneArmedContingencies(afterChoice);
                AppendEvents(events, choiceEvents, ref order);
                caster = FindLivingCaster(s, caster.InstanceId) ?? caster;
                continue;
            }

            var effectSource = CreateSource(
                caster, snapshot, effectIndex, effect);
            var (afterEffect, effectEvents) = EffectApplier.Apply(
                s,
                caster,
                effect,
                rng,
                catalog,
                effectSource,
                playingCardInstanceId: card.InstanceId,
                sourcePoolClass: snapshot.EffectivePoolClass(definition));
            s = BattleEngine.PruneArmedContingencies(afterEffect);
            AppendEvents(events, effectEvents, ref order);
            caster = FindLivingCaster(s, caster.InstanceId) ?? caster;
        }

        var completion = CompleteCardDestination(s, card.InstanceId, catalog);
        s = completion.State;
        events.Add(new BattleEvent(
            BattleEventKind.ContingencyResolved,
            Order: order++,
            CasterInstanceId: caster.InstanceId,
            CardId: definition.Id,
            Note: completion.Destination,
            ReenactedCard: snapshot));
        s = BattleEngine.RecordRepriseEligibleResolution(s, snapshot);
        var traitResult = CharacterTraitRuntime.TryTrigger(
            s,
            caster.InstanceId,
            CharacterTraitTrigger.OnContingencyTriggered,
            catalog,
            rng,
            orderStart: order);
        s = traitResult.State;
        AppendEvents(events, traitResult.Events, ref order);

        if (resolveAddedAttacksImmediately)
        {
            var (afterAttacks, attackEvents) =
                PlayerAttackingResolver.ResolveAddedAttacks(
                    s,
                    caster.InstanceId,
                    beforeSingleCount,
                    beforeRandomCount,
                    beforeAllCount,
                    rng,
                    catalog,
                    order);
            s = afterAttacks;
            events.AddRange(attackEvents);
            order += attackEvents.Count;
        }

        return new Resolution(s, events, PausedForChoice: false);
    }

    internal sealed record CardDestinationCompletion(
        BattleState State,
        string Destination);

    /// <summary>
    /// Finishes the pile movement after a Contingency effect list and reports
    /// the card's authoritative zone. ResolveCardChoice owns invocation for a
    /// resumed choice because PendingCardPlay belongs to BattleEngine.
    /// </summary>
    internal static CardDestinationCompletion CompleteCardDestination(
        BattleState state,
        string cardInstanceId,
        DataCatalog catalog)
    {
        int exhaustIndex = FindCardIndex(state.ExhaustPile, cardInstanceId);
        if (exhaustIndex < 0)
        {
            var pruned = BattleEngine.PruneArmedContingencies(state);
            return new CardDestinationCompletion(
                pruned,
                FindCardDestination(pruned, cardInstanceId));
        }

        var card = state.ExhaustPile[exhaustIndex];
        if (!catalog.TryGetCard(card.CardDefinitionId, out var definition))
            throw new InvalidOperationException(
                $"Contingency card definition '{card.CardDefinitionId}' does not exist.");

        bool hasExhaust = definition.EffectiveKeywords(card.IsUpgraded)
            ?.Contains("exhaust", StringComparer.Ordinal) == true;
        if (hasExhaust)
        {
            return new CardDestinationCompletion(
                BattleEngine.PruneArmedContingencies(state),
                "exhaust");
        }

        return new CardDestinationCompletion(
            BattleEngine.PruneArmedContingencies(state with
            {
                ExhaustPile = state.ExhaustPile.RemoveAt(exhaustIndex),
                DiscardPile = state.DiscardPile.Add(card),
            }),
            "discard");
    }

    private static string FindCardDestination(
        BattleState state,
        string cardInstanceId)
    {
        if (FindCardIndex(state.Hand, cardInstanceId) >= 0) return "hand";
        if (FindCardIndex(state.DrawPile, cardInstanceId) >= 0) return "draw";
        if (FindCardIndex(state.DiscardPile, cardInstanceId) >= 0) return "discard";
        if (FindCardIndex(state.ExhaustPile, cardInstanceId) >= 0) return "exhaust";
        if (FindCardIndex(state.SummonHeld, cardInstanceId) >= 0) return "summonHeld";
        if (FindCardIndex(state.PowerCards, cardInstanceId) >= 0) return "power";
        return "none";
    }

    private static PlayedCardEffectSnapshot CreateCurrentVersionSnapshot(
        BattleState state,
        ArmedContingency armed,
        BattleCardInstance card,
        CardDefinition definition)
    {
        var effects = card.IsUpgraded && definition.UpgradedEffects is not null
            ? definition.UpgradedEffects
            : definition.Effects;
        int originalCost = card.IsUpgraded
            ? definition.UpgradedCost ?? definition.Cost ?? 0
            : definition.Cost ?? 0;
        return new PlayedCardEffectSnapshot(
            definition.Id,
            card.InstanceId,
            armed.ActorInstanceId,
            definition.CardType,
            originalCost,
            PaidCost: 0,
            card.IsUpgraded,
            (definition.EffectiveKeywords(card.IsUpgraded)
                ?? Array.Empty<string>()).ToImmutableArray(),
            effects.ToImmutableArray(),
            PlaySequence: armed.ArmedOrdinal)
        {
            PoolClass = card.EffectivePoolClass(definition),
        };
    }

    private static CombatSource CreateSource(
        CombatActor caster,
        PlayedCardEffectSnapshot snapshot,
        int effectIndex,
        CardEffect effect) =>
        new(
            CombatSourceKind.CardEffect,
            OwnerSlot: caster.OwnerPlayerSlot ?? caster.SlotIndex,
            OwnerInstanceId: caster.OwnerActorInstanceId ?? caster.InstanceId,
            ActorInstanceId: caster.InstanceId,
            DefinitionId: snapshot.CardDefinitionId,
            InstanceId: snapshot.CardInstanceId,
            EffectIndex: effectIndex,
            Scope: effect.Scope.ToString().ToLowerInvariant());

    private static List<Candidate> SnapshotCandidates(
        BattleState state,
        Func<ArmedContingency, bool> matches)
    {
        var candidates = new List<Candidate>();
        if (state.Participants.IsDefaultOrEmpty)
        {
            foreach (var armed in state.ArmedContingencies)
            {
                if (matches(armed))
                    candidates.Add(new Candidate(
                        ParticipantId: null,
                        armed.CardInstanceId,
                        armed.ArmedOrdinal,
                        OwnerOrder: 0));
            }
        }
        else
        {
            for (int participantIndex = 0;
                 participantIndex < state.Participants.Length;
                 participantIndex++)
            {
                var participant = state.Participants[participantIndex];
                foreach (var armed in participant.ArmedContingencies)
                {
                    if (matches(armed))
                        candidates.Add(new Candidate(
                            participant.ParticipantId,
                            armed.CardInstanceId,
                            armed.ArmedOrdinal,
                            participantIndex));
                }
            }
        }

        return candidates
            .OrderBy(candidate => candidate.ArmedOrdinal)
            .ThenBy(candidate => candidate.OwnerOrder)
            .ToList();
    }

    private static BattleState RemoveArmedRecord(
        BattleState state,
        ArmedContingency armed) =>
        state with
        {
            ArmedContingencies = state.ArmedContingencies
                .Where(current => current.ArmedOrdinal != armed.ArmedOrdinal
                    || !string.Equals(
                        current.CardInstanceId,
                        armed.CardInstanceId,
                        StringComparison.Ordinal))
                .ToImmutableArray(),
        };

    private static int FindCardIndex(
        ImmutableArray<BattleCardInstance> pile,
        string instanceId)
    {
        for (int index = 0; index < pile.Length; index++)
        {
            if (string.Equals(
                    pile[index].InstanceId,
                    instanceId,
                    StringComparison.Ordinal))
                return index;
        }

        return -1;
    }

    private static CombatActor? FindLivingCaster(
        BattleState state,
        string actorInstanceId) =>
        state.Allies.FirstOrDefault(actor =>
            actor.IsAlive
            && string.Equals(
                actor.InstanceId,
                actorInstanceId,
                StringComparison.Ordinal));

    private static bool HasPendingChoice(BattleState state) =>
        state.PendingCardPlay is not null
        || (!state.Participants.IsDefaultOrEmpty
            && state.Participants.Any(participant =>
                participant.PendingCardPlay is not null));

    private static void AppendEvents(
        List<BattleEvent> destination,
        IReadOnlyList<BattleEvent> source,
        ref int order)
    {
        foreach (var battleEvent in source)
            destination.Add(battleEvent with { Order = order++ });
    }

    private sealed record Candidate(
        string? ParticipantId,
        string CardInstanceId,
        long ArmedOrdinal,
        int OwnerOrder);
}
