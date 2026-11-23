using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

public static partial class BattleEngine
{
    /// <summary>
    /// Toggles one participant's Ready state. Planning remains reversible until
    /// every living participant is Ready; that final transition atomically
    /// invokes the existing queued-combat resolution pipeline.
    /// </summary>
    public static (BattleState, IReadOnlyList<BattleEvent>) SetParticipantReady(
        BattleState state,
        string participantId,
        bool isReady,
        IRng rng,
        DataCatalog catalog)
    {
        var detailed = SetParticipantReadyDetailed(
            state, participantId, isReady, rng, catalog);
        return (detailed.State, detailed.LegacyEvents);
    }

    /// <summary>
    /// Detailed Ready transition for replay/journal consumers. CPU card
    /// decisions remain structured and keep their command-time snapshots;
    /// shared turn-resolution events deliberately exclude those card events.
    /// </summary>
    public static BattleReadyTransitionResult SetParticipantReadyDetailed(
        BattleState state,
        string participantId,
        bool isReady,
        IRng rng,
        DataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(participantId);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(catalog);
        if (state.Phase != BattlePhase.PlayerInput)
            throw new InvalidOperationException(
                $"Ready changes require Phase=PlayerInput, got {state.Phase}");
        if (state.Outcome != RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending)
            throw new InvalidOperationException("Ready changes require a pending battle");

        var initialized = BattleCpuPlanningCoordinator.EnsureParticipants(
            state, rng, catalog);
        if (!BattleParticipantAccess.TryGet(
                initialized, participantId, out var participant))
            throw new InvalidOperationException(
                $"Battle participant '{participantId}' does not exist.");
        if (!IsParticipantActorAlive(initialized, participant))
            throw new InvalidOperationException(
                $"Battle participant '{participantId}' has no living actor.");
        if (participant.PendingCardPlay is not null)
            throw new InvalidOperationException(
                $"Battle participant '{participantId}' must resolve its card choice before Ready.");

        // Mirror the former Server preflight inside Core before committing the
        // Ready flag. This closes the race where an already-Ready CPU gained a
        // new legal action immediately before the human Ready command.
        BattleCpuPlanningResult? preReadyCpuPlanning = null;
        var current = initialized;
        if (isReady)
        {
            preReadyCpuPlanning =
                BattleCpuPlanningCoordinator.PlanAfterExternalStateChange(
                    current, rng, catalog);
            current = preReadyCpuPlanning.State;
            if (!BattleParticipantAccess.TryGet(
                    current, participantId, out participant))
                throw new InvalidOperationException(
                    $"Battle participant '{participantId}' disappeared during CPU planning.");
        }

        current = BattleParticipantAccess.Replace(
            current, participant with { IsReady = isReady });
        current = current with
        {
            ActionRevision = IncrementRevision(current.ActionRevision),
        };

        ImmutableArray<BattleEvent> readyEvents =
        [
            new BattleEvent(
                BattleEventKind.ParticipantReadyChanged,
                Order: 0,
                CasterInstanceId: participant.ActorInstanceId,
                Amount: isReady ? 1 : 0,
                Note: participantId),
        ];

        if (!isReady)
            return BattleReadyTransitionResult.WithoutResolution(
                current, readyEvents);

        if (!AllActiveParticipantsReady(current))
        {
            return new BattleReadyTransitionResult(
                current,
                readyEvents,
                ResolutionEvents: [],
                PreReadyCpuPlanning: preReadyCpuPlanning,
                NextTurnCpuPlanning: null)
            {
                StateAfterReady = current,
                StateAfterResolutionBeforeNextTurnPlanning = current,
            };
        }

        var resolution = ResolveQueuedTurnDetailed(
            current, rng, catalog);
        var sharedResolutionEvents = ImmutableArray.CreateBuilder<BattleEvent>(
            resolution.ResolutionEvents.Length + 1);
        sharedResolutionEvents.Add(new BattleEvent(
            BattleEventKind.ResolutionStarted,
            Order: 0,
            Note: $"turn={current.Turn}"));
        foreach (var battleEvent in resolution.ResolutionEvents)
        {
            sharedResolutionEvents.Add(battleEvent with
            {
                Order = sharedResolutionEvents.Count,
            });
        }

        return new BattleReadyTransitionResult(
            resolution.State,
            readyEvents,
            sharedResolutionEvents.ToImmutable(),
            preReadyCpuPlanning,
            resolution.NextTurnCpuPlanning)
        {
            StateAfterReady = current,
            StateAfterResolutionBeforeNextTurnPlanning =
                resolution.StateBeforeNextTurnCpuPlanning,
        };
    }

    public static bool AllActiveParticipantsReady(BattleState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var participants = BattleParticipantAccess.EffectiveParticipants(state);
        bool foundActive = false;
        foreach (var participant in participants)
        {
            if (!IsParticipantActorAlive(state, participant))
                continue;
            foundActive = true;
            if (!participant.IsReady)
                return false;
        }
        return foundActive;
    }

    private static bool IsParticipantActorAlive(
        BattleState state,
        BattleParticipantState participant) =>
        state.Allies.Any(actor => actor.IsAlive && string.Equals(
            actor.InstanceId, participant.ActorInstanceId, StringComparison.Ordinal));

    private static long IncrementRevision(long revision) =>
        revision >= long.MaxValue ? long.MaxValue : Math.Max(0L, revision) + 1L;
}

/// <summary>
/// One reversible Ready command and, when the barrier closes, its atomic turn
/// resolution. Structured CPU decisions are kept outside ResolutionEvents so
/// journal consumers never need to reconstruct them from PlayCard markers.
/// </summary>
public sealed record BattleReadyTransitionResult(
    BattleState State,
    ImmutableArray<BattleEvent> ReadyEvents,
    ImmutableArray<BattleEvent> ResolutionEvents,
    BattleCpuPlanningResult? PreReadyCpuPlanning,
    BattleCpuPlanningResult? NextTurnCpuPlanning)
{
    /// <summary>State immediately after the Ready flag, before queued attacks resolve.</summary>
    public BattleState StateAfterReady { get; init; } = State;

    /// <summary>
    /// State after shared resolution/turn-start processing and before any CPU
    /// participant commits an action for the new turn.
    /// </summary>
    public BattleState StateAfterResolutionBeforeNextTurnPlanning { get; init; } = State;

    public bool DidResolve => !ResolutionEvents.IsDefaultOrEmpty;

    public ImmutableArray<CpuCommittedAction> CpuActionsBeforeResolution =>
        PreReadyCpuPlanning?.Actions ?? ImmutableArray<CpuCommittedAction>.Empty;

    public ImmutableArray<CpuCommittedAction> CpuActionsAfterResolution =>
        NextTurnCpuPlanning?.Actions ?? ImmutableArray<CpuCommittedAction>.Empty;

    public ImmutableArray<CpuCommittedAction> CpuActions =>
        CpuActionsBeforeResolution.AddRange(CpuActionsAfterResolution);

    /// <summary>
    /// Chronological detailed event stream: pre-Ready CPU planning, Ready,
    /// shared resolution, then next-turn CPU planning.
    /// </summary>
    public ImmutableArray<BattleEvent> Events => ComposeEvents(cpuBeforeReady: true);

    /// <summary>
    /// Old tuple API compatibility order. Historically Core emitted the Ready
    /// marker before the CPU card events it planned in that call.
    /// </summary>
    internal ImmutableArray<BattleEvent> LegacyEvents =>
        ComposeEvents(cpuBeforeReady: false);

    // Transitional alias for consumers built against the first detailed draft.
    public BattleCpuPlanningResult? ReadyCpuPlanning => PreReadyCpuPlanning;

    private ImmutableArray<BattleEvent> ComposeEvents(bool cpuBeforeReady)
    {
        var events = ImmutableArray.CreateBuilder<BattleEvent>();
        if (cpuBeforeReady)
            AppendCpu(CpuActionsBeforeResolution);
        Append(ReadyEvents);
        if (!cpuBeforeReady)
            AppendCpu(CpuActionsBeforeResolution);
        Append(ResolutionEvents);
        AppendCpu(CpuActionsAfterResolution);
        return events.ToImmutable();

        void AppendCpu(IEnumerable<CpuCommittedAction> actions)
        {
            foreach (var action in actions)
                Append(action.Events);
        }

        void Append(IEnumerable<BattleEvent> source)
        {
            foreach (var battleEvent in source)
                events.Add(battleEvent with { Order = events.Count });
        }
    }

    internal static BattleReadyTransitionResult WithoutResolution(
        BattleState state,
        ImmutableArray<BattleEvent> readyEvents) =>
        new(state, readyEvents, [], null, null)
        {
            StateAfterReady = state,
            StateAfterResolutionBeforeNextTurnPlanning = state,
        };
}
