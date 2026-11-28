using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// Discovers card-controlled allied actors and runs their deterministic
/// planning commands. This class does not resolve queued combat; it only uses
/// the same card commands as a human participant and marks a CPU ready when it
/// has no further legal action (or a safety guard stops it).
/// </summary>
public static class BattleCpuPlanningCoordinator
{
    public const int MaxPlaysPerActor = 30;
    public const int MaxTotalPlays = 100;

    /// <summary>
    /// Creates missing participant state for every living card-controlled ally.
    /// Existing participants are never reset. Deck expansion, shuffle and
    /// initial draw are deterministic for the supplied RNG stream.
    /// </summary>
    public static BattleState EnsureParticipants(
        BattleState state,
        IRng rng,
        DataCatalog catalog)
        => EnsureParticipantsCore(state, rng, catalog, out _);

    /// <summary>
    /// Plans every unready CPU participant in stable actor-slot order. A CPU
    /// summoned by a played card is discovered after that card completes and is
    /// planned immediately before its summoner continues.
    /// </summary>
    public static BattleCpuPlanningResult PlanUntilReady(
        BattleState state,
        IRng rng,
        DataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(catalog);

        var initialized = ImmutableArray.CreateBuilder<string>();
        var actions = ImmutableArray.CreateBuilder<CpuCommittedAction>();
        var stops = ImmutableArray.CreateBuilder<CpuPlanningStop>();
        var playsByParticipant = BattleParticipantAccess.EffectiveParticipants(state)
            .Where(participant =>
                participant.ControllerKind == BattleParticipantControllerKind.Cpu)
            .ToDictionary(
                participant => participant.ParticipantId,
                participant => Math.Max(0, participant.CpuCardPlaysThisTurn),
                StringComparer.Ordinal);
        var fingerprintsByParticipant = new Dictionary<string, HashSet<string>>(
            StringComparer.Ordinal);
        long nextPlanningOrdinal = 0;
        int legacyParticipantTotal = playsByParticipant.Values.Aggregate(
            0,
            (total, count) =>
                total >= MaxTotalPlays - Math.Min(count, MaxTotalPlays)
                    ? MaxTotalPlays
                    : total + Math.Min(count, MaxTotalPlays));
        int totalPlays = Math.Max(
            Math.Clamp(state.CpuTotalCardPlaysThisTurn, 0, MaxTotalPlays),
            legacyParticipantTotal);

        var current = EnsureParticipantsCore(state, rng, catalog, out var initialIds);
        var stateBeforePlanning = current;
        initialized.AddRange(initialIds);

        // Snapshot the initial participant order. Newly created participants
        // are handled recursively immediately after their creating card.
        var initialCpuIds = BattleParticipantAccess.EffectiveParticipants(current)
            .Where(IsUnreadyCpu)
            .OrderBy(participant => GetActorSlot(current, participant))
            .Select(participant => participant.ParticipantId)
            .ToArray();

        foreach (var participantId in initialCpuIds)
        {
            current = PlanParticipant(
                current,
                participantId,
                rng,
                catalog,
                initialized,
                actions,
                stops,
                playsByParticipant,
                fingerprintsByParticipant,
                ref totalPlays,
                ref nextPlanningOrdinal);

            if (totalPlays >= MaxTotalPlays)
                break;
        }

        // If the global cap interrupted the stable-order loop, every remaining
        // CPU must still become ready so Ready synchronization cannot deadlock.
        if (totalPlays >= MaxTotalPlays)
        {
            foreach (var participant in BattleParticipantAccess.InActorSlotOrder(current)
                         .Where(IsUnreadyCpu))
            {
                current = CommitStop(
                    current,
                    participant.ParticipantId,
                    CpuPlanningStopReason.TotalPlayCap,
                    playsByParticipant.GetValueOrDefault(participant.ParticipantId),
                    stops,
                    catalog,
                    ref nextPlanningOrdinal);
            }
        }

        return new BattleCpuPlanningResult(
            current,
            actions.ToImmutable(),
            stops.ToImmutable(),
            initialized.ToImmutable())
        {
            StateBeforePlanning = stateBeforePlanning,
            ReopenedParticipantIds = actions
                .SelectMany(action => action.ReopenedParticipantIds)
                .Distinct(StringComparer.Ordinal)
                .ToImmutableArray(),
        };
    }

    /// <summary>
    /// Rechecks CPUs that had already declared Ready after another participant
    /// changed battle state.  Only a CPU with a newly legal card is reopened;
    /// previously committed actions remain untouched and the CPU immediately
    /// appends any newly possible actions before becoming Ready again.
    /// </summary>
    public static BattleCpuPlanningResult PlanAfterExternalStateChange(
        BattleState state,
        IRng rng,
        DataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(catalog);

        var initialized = EnsureParticipantsCore(
            state, rng, catalog, out var initializedIds);
        var reopened = ReopenReadyCpusWithLegalActions(initialized, catalog);
        var planned = PlanUntilReady(reopened.State, rng, catalog);
        return planned with
        {
            StateBeforePlanning = planned.StateBeforePlanning ?? reopened.State,
            InitializedParticipantIds = initializedIds
                .AddRange(planned.InitializedParticipantIds)
                .Distinct(StringComparer.Ordinal)
                .ToImmutableArray(),
            ReopenedParticipantIds = reopened.ParticipantIds
                .Concat(planned.ReopenedParticipantIds)
                .Distinct(StringComparer.Ordinal)
                .ToImmutableArray(),
        };
    }

    /// <summary>
    /// Applies the post-command readiness rule. A different human participant
    /// is automatically un-readied only when the command added a genuinely
    /// new playable card action. Ready CPUs are then rechecked and immediately
    /// append their new deterministic actions. Previously committed actions
    /// are never recomputed.
    /// </summary>
    public static BattleCpuPlanningResult PlanAfterCommittedAction(
        BattleState before,
        BattleState after,
        string actingParticipantId,
        IRng rng,
        DataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentException.ThrowIfNullOrWhiteSpace(actingParticipantId);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(catalog);

        var reopenedByCommand = ReopenReadyHumansWithNewLegalActions(
            before, after, actingParticipantId, catalog);
        var beforeCpuPlanning = reopenedByCommand.State;
        var planned = PlanAfterExternalStateChange(
            beforeCpuPlanning, rng, catalog);
        return planned with
        {
            ReopenedParticipantIds = reopenedByCommand.ParticipantIds
                .Concat(planned.ReopenedParticipantIds)
                .Distinct(StringComparer.Ordinal)
                .ToImmutableArray(),
        };
    }

    private static ReopenedCpuParticipants ReopenReadyHumansWithNewLegalActions(
        BattleState before,
        BattleState after,
        string? excludedParticipantId,
        DataCatalog catalog)
    {
        var current = after;
        var reopened = ImmutableArray.CreateBuilder<string>();
        foreach (var participant in BattleParticipantAccess.EffectiveParticipants(after))
        {
            if (participant.ControllerKind != BattleParticipantControllerKind.Human
                || !participant.IsReady
                || string.Equals(
                    participant.ParticipantId,
                    excludedParticipantId,
                    StringComparison.Ordinal)
                || !BattleParticipantAccess.TryGet(
                    before, participant.ParticipantId, out var previous))
                continue;

            var beforeLegal = GetLegalActionKeys(
                before, previous.ParticipantId, catalog);
            var afterLegal = GetLegalActionKeys(
                current, participant.ParticipantId, catalog);
            if (!afterLegal.Any(action => !beforeLegal.Contains(action)))
                continue;

            current = BattleParticipantAccess.Replace(
                current, participant with { IsReady = false });
            reopened.Add(participant.ParticipantId);
        }

        return new ReopenedCpuParticipants(current, reopened.ToImmutable());
    }

    private static BattleState PlanParticipant(
        BattleState state,
        string participantId,
        IRng rng,
        DataCatalog catalog,
        ImmutableArray<string>.Builder initialized,
        ImmutableArray<CpuCommittedAction>.Builder actions,
        ImmutableArray<CpuPlanningStop>.Builder stops,
        Dictionary<string, int> playsByParticipant,
        Dictionary<string, HashSet<string>> fingerprintsByParticipant,
        ref int totalPlays,
        ref long nextPlanningOrdinal)
    {
        var current = state;
        if (!BattleParticipantAccess.TryGet(current, participantId, out var participant)
            || participant.ControllerKind != BattleParticipantControllerKind.Cpu
            || participant.IsReady)
            return current;

        var seen = fingerprintsByParticipant.GetValueOrDefault(participantId);
        if (seen is null)
        {
            seen = new HashSet<string>(StringComparer.Ordinal);
            fingerprintsByParticipant.Add(participantId, seen);
        }

        while (true)
        {
            if (!CanContinuePlanning(current, participantId))
            {
                current = CommitStop(
                    current,
                    participantId,
                    CpuPlanningStopReason.ActorUnavailable,
                    playsByParticipant.GetValueOrDefault(participantId),
                    stops,
                    catalog,
                    ref nextPlanningOrdinal);
                return current;
            }

            int actorPlays = playsByParticipant.GetValueOrDefault(participantId);
            if (actorPlays >= MaxPlaysPerActor)
            {
                current = CommitStop(
                    current,
                    participantId,
                    CpuPlanningStopReason.PerActorPlayCap,
                    actorPlays,
                    stops,
                    catalog,
                    ref nextPlanningOrdinal);
                return current;
            }
            if (totalPlays >= MaxTotalPlays)
                return current;

            string fingerprint = BuildPlanningFingerprint(current, participantId);
            if (!seen.Add(fingerprint))
            {
                current = CommitStop(
                    current,
                    participantId,
                    CpuPlanningStopReason.RepeatedState,
                    actorPlays,
                    stops,
                    catalog,
                    ref nextPlanningOrdinal);
                return current;
            }

            var decision = CpuCardPlayPolicy.ChooseNext(current, participantId, catalog);
            if (decision is null)
            {
                current = CommitStop(
                    current,
                    participantId,
                    CpuPlanningStopReason.NoLegalAction,
                    actorPlays,
                    stops,
                    catalog,
                    ref nextPlanningOrdinal);
                return current;
            }

            var decisionState = current;
            var projectedBefore = BattleParticipantAccess.ProjectToRoot(
                decisionState, participantId);
            var cardSnapshot = projectedBefore.Hand[decision.HandIndex];
            if (!catalog.TryGetCard(cardSnapshot.CardDefinitionId, out var cardDefinition))
                throw new InvalidOperationException(
                    $"CPU selected unknown card '{cardSnapshot.CardDefinitionId}'.");
            var actorBefore = decisionState.Allies.First(actor =>
                string.Equals(
                    actor.InstanceId,
                    decision.ActorInstanceId,
                    StringComparison.Ordinal));
            var costBreakdown = BattleCardCostCalculator.EvaluateForHand(
                projectedBefore, cardSnapshot, cardDefinition, actorBefore);
            int usesBefore = projectedBefore.CardUsesThisTurn
                .GetValueOrDefault(cardDefinition.Id);
            long revisionBefore = decisionState.ActionRevision;
            var selectedInstanceIds = ImmutableArray.CreateBuilder<string>();

            var actionEvents = new List<BattleEvent>();
            var (afterPlay, playEvents) = BattleEngine.PlayCard(
                current,
                participantId,
                decision.HandIndex,
                decision.TargetEnemyIndex,
                decision.TargetAllyIndex,
                rng,
                catalog);
            current = afterPlay;
            AppendEvents(actionEvents, playEvents);

            while (BattleParticipantAccess.TryGet(current, participantId, out var afterParticipant)
                   && afterParticipant.PendingCardPlay is { } pending)
            {
                if (pending.Choice.Count < 0
                    || pending.Choice.CandidateInstanceIds.Length < pending.Choice.Count)
                    throw new InvalidOperationException(
                        $"CPU pending choice '{pending.Choice.Action}' has " +
                        $"{pending.Choice.CandidateInstanceIds.Length} candidates for " +
                        $"required count {pending.Choice.Count}.");

                var selected = pending.Choice.CandidateInstanceIds
                    .Take(pending.Choice.Count)
                    .ToImmutableArray();
                selectedInstanceIds.AddRange(selected);
                var (afterChoice, choiceEvents) = BattleEngine.ResolveCardChoice(
                    current, participantId, selected, rng, catalog);
                current = afterChoice;
                AppendEvents(actionEvents, choiceEvents);
            }

            actorPlays++;
            totalPlays++;
            current = current with
            {
                CpuTotalCardPlaysThisTurn = totalPlays,
            };
            playsByParticipant[participantId] = actorPlays;
            if (BattleParticipantAccess.TryGet(
                    current, participantId, out var committedParticipant))
            {
                current = BattleParticipantAccess.Replace(
                    current,
                    committedParticipant with
                    {
                        CpuCardPlaysThisTurn = actorPlays,
                    });
            }
            // A CPU card is an independently committed action. If it creates a
            // new legal action for an already-Ready human, reopen that human at
            // this exact causal boundary so replay ordering and snapshots do
            // not attribute the change to the command that triggered planning.
            var reopenedHumans = ReopenReadyHumansWithNewLegalActions(
                decisionState,
                current,
                excludedParticipantId: null,
                catalog);
            current = reopenedHumans.State;
            var actorAfter = current.Allies.FirstOrDefault(actor =>
                string.Equals(
                    actor.InstanceId,
                    decision.ActorInstanceId,
                    StringComparison.Ordinal));
            current = EnsureParticipantsCore(current, rng, catalog, out var newIds);
            var reopened = ReopenReadyCpusWithLegalActions(current, catalog);
            current = reopened.State;
            actions.Add(new CpuCommittedAction(
                participantId,
                decision.ActorInstanceId,
                decision.CardInstanceId,
                decision.CardDefinitionId,
                decision.PayableCost,
                decision.TargetEnemyIndex,
                decision.TargetAllyIndex,
                decision.DebugReason,
                actionEvents.ToImmutableArray())
            {
                CardSnapshot = cardSnapshot,
                CostBreakdown = costBreakdown,
                UsesBefore = usesBefore,
                RevisionBefore = revisionBefore,
                RevisionAfter = current.ActionRevision,
                TargetActorInstanceIds = ResolveTargetActorInstanceIds(
                    decisionState,
                    cardDefinition,
                    cardSnapshot.IsUpgraded,
                    decision.TargetEnemyIndex,
                    decision.TargetAllyIndex),
                SelectedInstanceIds = selectedInstanceIds.ToImmutable(),
                IntentDelta = BuildIntentDelta(actorBefore, actorAfter),
                ActorContext = new CpuCommittedActorContext(
                    Strength: actorBefore.GetStatus(BattleStatusIds.Strength),
                    Weak: actorBefore.GetStatus(BattleStatusIds.Weak),
                    Dexterity: actorBefore.GetStatus(BattleStatusIds.Dexterity))
                {
                    AttackPower = actorBefore.AttackPower,
                    DefensePower = actorBefore.DefensePower,
                },
                Turn = decisionState.Turn,
                StateBefore = decisionState,
                StateAfter = current,
                ReopenedParticipantIds = reopenedHumans.ParticipantIds
                    .AddRange(reopened.ParticipantIds)
                    .Distinct(StringComparer.Ordinal)
                    .ToImmutableArray(),
                PlanningOrdinal = nextPlanningOrdinal++,
            });

            if (!newIds.IsDefaultOrEmpty)
            {
                initialized.AddRange(newIds);
                foreach (var newParticipantId in OrderParticipantIdsByActorSlot(
                             current, newIds))
                {
                    current = PlanParticipant(
                        current,
                        newParticipantId,
                        rng,
                        catalog,
                        initialized,
                        actions,
                        stops,
                        playsByParticipant,
                        fingerprintsByParticipant,
                        ref totalPlays,
                        ref nextPlanningOrdinal);

                    if (totalPlays >= MaxTotalPlays)
                        return current;
                }
            }

            foreach (var reopenedParticipantId in OrderParticipantIdsByActorSlot(
                         current, reopened.ParticipantIds))
            {
                current = PlanParticipant(
                    current,
                    reopenedParticipantId,
                    rng,
                    catalog,
                    initialized,
                    actions,
                    stops,
                    playsByParticipant,
                    fingerprintsByParticipant,
                    ref totalPlays,
                    ref nextPlanningOrdinal);

                if (totalPlays >= MaxTotalPlays)
                    return current;
            }
        }
    }

    private static ReopenedCpuParticipants ReopenReadyCpusWithLegalActions(
        BattleState state,
        DataCatalog catalog)
    {
        var current = state;
        var reopened = ImmutableArray.CreateBuilder<string>();
        foreach (var participant in BattleParticipantAccess.InActorSlotOrder(state))
        {
            if (participant.ControllerKind != BattleParticipantControllerKind.Cpu
                || !participant.IsReady
                || participant.CpuCardPlaysThisTurn >= MaxPlaysPerActor
                || !GetLegalActionKeys(current, participant.ParticipantId, catalog)
                    .Any(action =>
                        !participant.CpuLegalActionKeysWhenReadied.Contains(action)))
                continue;

            current = BattleParticipantAccess.Replace(
                current, participant with
                {
                    IsReady = false,
                    CpuLegalActionKeysWhenReadied = ImmutableHashSet<string>.Empty,
                });
            reopened.Add(participant.ParticipantId);
        }

        return new ReopenedCpuParticipants(current, reopened.ToImmutable());
    }

    private static IEnumerable<string> OrderParticipantIdsByActorSlot(
        BattleState state,
        IEnumerable<string> participantIds) => participantIds
        .Select((participantId, index) => new
        {
            ParticipantId = participantId,
            Index = index,
            Slot = BattleParticipantAccess.TryGet(
                    state, participantId, out var participant)
                ? GetActorSlot(state, participant)
                : int.MaxValue,
        })
        .OrderBy(item => item.Slot)
        .ThenBy(item => item.Index)
        .Select(item => item.ParticipantId);

    private static int GetActorSlot(
        BattleState state,
        BattleParticipantState participant) => state.Allies
        .FirstOrDefault(actor => string.Equals(
            actor.InstanceId,
            participant.ActorInstanceId,
            StringComparison.Ordinal))
        ?.SlotIndex ?? int.MaxValue;

    private static HashSet<string> GetLegalActionKeys(
        BattleState state,
        string participantId,
        DataCatalog catalog)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (state.Phase != BattlePhase.PlayerInput
            || state.Outcome !=
                RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending
            || !BattleParticipantAccess.TryGet(state, participantId, out var participant)
            || participant.PendingCardPlay is not null)
            return result;

        var actor = state.Allies.FirstOrDefault(candidate =>
            candidate.IsAlive
            && string.Equals(
                candidate.InstanceId,
                participant.ActorInstanceId,
                StringComparison.Ordinal));
        if (actor is null)
            return result;

        var projected = BattleParticipantAccess.ProjectToRoot(state, participantId);
        foreach (var card in projected.Hand)
        {
            if (!catalog.TryGetCard(card.CardDefinitionId, out var definition))
                continue;
            int usesPerTurn = definition.EffectiveUsesPerTurn(card.IsUpgraded);
            if (CardUsageTracker.HasReachedLimit(
                    projected, definition.Id, usesPerTurn)
                || BattleCardCostCalculator.GetPlayRestrictionReason(
                    projected, actor, definition) is not null
                || definition.ComboGaugeCost < 0
                || projected.ComboGauge < definition.ComboGaugeCost)
                continue;

            var cost = BattleCardCostCalculator.EvaluateForHand(
                projected, card, definition, actor);
            if (cost.PayableCost is { } payableCost
                && projected.Energy >= payableCost)
                result.Add($"card:{card.InstanceId}");
        }

        if (actor.GetStatus(BattleStatusIds.Seal) <= 0)
        {
            IReadOnlyList<CharacterSkillDefinition> skills =
                catalog.TryGetCharacter(participant.CharacterId, out var character)
                    ? character.EffectiveSkills
                    : catalog.TryGetUnit(participant.CharacterId, out var unit)
                        ? unit.EffectiveSkills
                        : Array.Empty<CharacterSkillDefinition>();
            foreach (var skill in skills)
            {
                int uses = projected.CharacterSkillUsesThisTurn
                    .GetValueOrDefault(skill.Id);
                if (skill.Cost.IsValid
                    && projected.Energy >= skill.Cost.Energy
                    && projected.ComboGauge >= skill.Cost.ComboGauge
                    && (!skill.RequiresCardsInHand || !projected.Hand.IsEmpty)
                    && (!skill.RequiresPhase || actor.Phase != ActorPhase.None)
                    && (skill.EffectiveUsesPerTurn <= 0
                        || uses < skill.EffectiveUsesPerTurn))
                {
                    result.Add($"skill:{skill.Id}");
                }
            }
        }

        for (int index = 0; index < projected.Potions.Length; index++)
        {
            string potionId = projected.Potions[index];
            if (string.IsNullOrEmpty(potionId)
                || !catalog.TryGetPotion(potionId, out var potion)
                || PotionUsageTracker.HasReachedLimit(
                    projected, potion.Id, potion.UsesPerTurn))
                continue;
            result.Add($"potion:{index}:{potion.Id}");
        }

        return result;
    }

    private static BattleState EnsureParticipantsCore(
        BattleState state,
        IRng rng,
        DataCatalog catalog,
        out ImmutableArray<string> initializedParticipantIds)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(catalog);

        // Planning is a PlayerInput-only mutation. In particular, a lethal
        // command may also have summoned a card-controlled ally; do not consume
        // RNG or initialize that ally after BattleEnd has already committed.
        if (state.Phase != BattlePhase.PlayerInput
            || state.Outcome !=
                RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending)
        {
            initializedParticipantIds = ImmutableArray<string>.Empty;
            return state;
        }

        var current = state;
        var initialized = ImmutableArray.CreateBuilder<string>();

        // Adding a CPU to a legacy snapshot must not accidentally make that CPU
        // the compatibility primary participant.
        if (current.Participants.IsDefaultOrEmpty && current.Allies.Any())
            current = BattleParticipantAccess.InitializePrimary(current);

        foreach (var actor in current.Allies
                     .Where(candidate => candidate.IsAlive)
                     .OrderBy(candidate => candidate.SlotIndex))
        {
            if (BattleParticipantAccess.TryGetByActor(current, actor.InstanceId, out _)
                || !catalog.TryGetUnit(actor.DefinitionId, out var unit)
                || !unit.IsCardControlled)
                continue;

            var deck = ExpandDeck(actor.InstanceId, unit, catalog);
            ShuffleInPlace(deck, rng);
            int drawCount = Math.Min(deck.Length, Math.Min(unit.DrawPerTurn, unit.HandLimit));
            int? targetEnemy = FirstLivingActorIndex(current.Enemies);
            int allyIndex = IndexOfActor(current.Allies, actor.InstanceId);
            string participantId = ParticipantIdFor(actor.InstanceId);

            var participant = new BattleParticipantState(
                ParticipantId: participantId,
                ActorInstanceId: actor.InstanceId,
                ControllerKind: BattleParticipantControllerKind.Cpu,
                OwnerPlayerSlot: actor.OwnerPlayerSlot,
                CharacterId: unit.Id,
                TargetAllyIndex: allyIndex,
                TargetEnemyIndex: targetEnemy,
                Energy: unit.InitialEnergy,
                EnergyPerTurn: unit.EnergyPerTurn,
                DrawPerTurn: unit.DrawPerTurn,
                DrawPile: deck.ToImmutableArray(),
                Hand: ImmutableArray<BattleCardInstance>.Empty,
                DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
                ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
                SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
                PowerCards: ImmutableArray<BattleCardInstance>.Empty,
                ComboCount: 0,
                MaxComboThisTurn: 0,
                ComboGauge: 0,
                LastPlayedOrigCost: null,
                NextCardComboFreePass: false,
                WildUsedInCurrentCombo: false,
                UsedCharacterSkillIdsThisTurn: ImmutableHashSet<string>.Empty,
                CharacterSkillUsesThisTurn: ImmutableDictionary<string, int>.Empty,
                CardUsesThisTurn: ImmutableDictionary<string, int>.Empty,
                CardUseLockedThisTurn: false,
                OwnedRelicIds: unit.EffectiveOwnedRelicIds.ToImmutableArray(),
                Potions: unit.EffectivePotions.ToImmutableArray(),
                PotionUsesThisTurn: ImmutableDictionary<string, int>.Empty,
                PendingCardPlay: null,
                IsReady: false);

            current = BattleParticipantAccess.Add(current, participant);
            // Initial cards enter this participant's hand through the same
            // boundary as every later draw.  In particular, Confusion belongs
            // to the summoned actor (not the compatibility primary), and the
            // unit-specific hand limit must apply from the first frame.
            var projected = BattleParticipantAccess.ProjectToRoot(
                current, participantId);
            projected = DrawHelper.Draw(
                projected,
                drawCount,
                rng,
                unit.HandLimit,
                actor,
                out _);
            current = BattleParticipantAccess.CommitRootToParticipant(
                projected, participantId);
            initialized.Add(participantId);
        }

        initializedParticipantIds = initialized.ToImmutable();
        return current;
    }

    private static BattleCardInstance[] ExpandDeck(
        string actorInstanceId,
        Battle.Definitions.UnitDefinition unit,
        DataCatalog catalog)
    {
        var cards = new List<BattleCardInstance>();
        int ordinal = 0;
        foreach (var entry in unit.EffectiveDeck)
        {
            if (!catalog.TryGetCard(entry.CardId, out _))
                throw new InvalidOperationException(
                    $"Card-controlled unit '{unit.Id}' references unknown card '{entry.CardId}'.");
            if (entry.Count < 1)
                throw new InvalidOperationException(
                    $"Card-controlled unit '{unit.Id}' has invalid count {entry.Count} for '{entry.CardId}'.");

            for (int copy = 0; copy < entry.Count; copy++)
            {
                cards.Add(new BattleCardInstance(
                    CardInstanceIdFor(actorInstanceId, ordinal++),
                    entry.CardId,
                    entry.Upgraded,
                    CostOverride: null));
            }
        }
        return cards.ToArray();
    }

    private static bool CanContinuePlanning(BattleState state, string participantId)
    {
        if (state.Phase != BattlePhase.PlayerInput
            || state.Outcome != RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending
            || !BattleParticipantAccess.TryGet(state, participantId, out var participant))
            return false;

        return state.Allies.Any(actor =>
            actor.IsAlive
            && string.Equals(
                actor.InstanceId, participant.ActorInstanceId, StringComparison.Ordinal));
    }

    private static bool IsUnreadyCpu(BattleParticipantState participant) =>
        participant.ControllerKind == BattleParticipantControllerKind.Cpu
        && !participant.IsReady;

    private static BattleState MarkReady(BattleState state, string participantId)
    {
        if (!BattleParticipantAccess.TryGet(state, participantId, out var participant)
            || participant.IsReady)
            return state;
        return BattleParticipantAccess.Replace(state, participant with { IsReady = true });
    }

    private static BattleState CommitStop(
        BattleState state,
        string participantId,
        CpuPlanningStopReason reason,
        int playsCommitted,
        ImmutableArray<CpuPlanningStop>.Builder stops,
        DataCatalog catalog,
        ref long nextPlanningOrdinal)
    {
        var legalActions = GetLegalActionKeys(state, participantId, catalog)
            .ToImmutableHashSet(StringComparer.Ordinal);
        var after = BattleParticipantAccess.TryGet(
                state, participantId, out var participant)
            ? BattleParticipantAccess.Replace(
                state,
                participant with
                {
                    IsReady = true,
                    CpuLegalActionKeysWhenReadied = legalActions,
                })
            : state;
        stops.Add(new CpuPlanningStop(participantId, reason, playsCommitted)
        {
            PlanningOrdinal = nextPlanningOrdinal++,
            StateBefore = state,
            StateAfter = after,
        });
        return after;
    }

    private static string ParticipantIdFor(string actorInstanceId) =>
        $"cpu:{actorInstanceId}";

    private static string CardInstanceIdFor(string actorInstanceId, int ordinal) =>
        $"cpu-card:{actorInstanceId}:{ordinal}";

    private static void ShuffleInPlace(BattleCardInstance[] cards, IRng rng)
    {
        for (int i = cards.Length - 1; i > 0; i--)
        {
            int j = rng.NextInt(0, i + 1);
            (cards[i], cards[j]) = (cards[j], cards[i]);
        }
    }

    private static int? FirstLivingActorIndex(ImmutableArray<CombatActor> actors)
    {
        for (int i = 0; i < actors.Length; i++)
            if (actors[i].IsAlive)
                return i;
        return null;
    }

    private static ImmutableArray<string> ResolveTargetActorInstanceIds(
        BattleState state,
        CardDefinition definition,
        bool upgraded,
        int? enemyIndex,
        int? allyIndex)
    {
        var effects = upgraded && definition.UpgradedEffects is not null
            ? definition.UpgradedEffects
            : definition.Effects;
        bool selectsEnemy = effects.Any(effect =>
            effect.Scope == EffectScope.Single
            && (effect.Action == "attack" || effect.Side == EffectSide.Enemy));
        bool selectsAlly = effects.Any(effect =>
            effect.Scope == EffectScope.Single
            && effect.Action != "attack"
            && effect.Side == EffectSide.Ally);

        var targets = ImmutableArray.CreateBuilder<string>();
        if (selectsEnemy
            && enemyIndex is { } ei
            && ei >= 0
            && ei < state.Enemies.Length)
            targets.Add(state.Enemies[ei].InstanceId);
        if (selectsAlly
            && allyIndex is { } ai
            && ai >= 0
            && ai < state.Allies.Length)
            targets.Add(state.Allies[ai].InstanceId);
        return targets.Distinct(StringComparer.Ordinal).ToImmutableArray();
    }

    private static CpuCommittedIntentDelta BuildIntentDelta(
        CombatActor before,
        CombatActor? after)
    {
        if (after is null)
            return CpuCommittedIntentDelta.Empty;
        return new CpuCommittedIntentDelta(
            AddedSingleHits: AddedHits(before.AttackSingle, after.AttackSingle),
            AddedRandomHits: AddedHits(before.AttackRandom, after.AttackRandom),
            AddedAllHits: AddedHits(before.AttackAll, after.AttackAll),
            BlockSumDelta: after.Block.Sum - before.Block.Sum,
            BlockAddCountDelta: after.Block.AddCount - before.Block.AddCount);
    }

    private static ImmutableArray<AttackHit> AddedHits(
        AttackPool before,
        AttackPool after)
    {
        if (after.Hits.Length <= before.Hits.Length)
            return ImmutableArray<AttackHit>.Empty;
        return after.Hits.Skip(before.Hits.Length).ToImmutableArray();
    }

    private static int IndexOfActor(
        ImmutableArray<CombatActor> actors,
        string actorInstanceId)
    {
        for (int i = 0; i < actors.Length; i++)
            if (string.Equals(actors[i].InstanceId, actorInstanceId, StringComparison.Ordinal))
                return i;
        return -1;
    }

    private static void AppendEvents(
        List<BattleEvent> destination,
        IReadOnlyList<BattleEvent> source)
    {
        foreach (var battleEvent in source)
            destination.Add(battleEvent with { Order = destination.Count });
    }

    private static string BuildPlanningFingerprint(
        BattleState state,
        string participantId)
    {
        var builder = new StringBuilder(512);
        builder.Append(state.Turn).Append('|')
            .Append((int)state.Phase).Append('|')
            .Append((int)state.Outcome).Append('|');

        foreach (var actor in state.Allies.Concat(state.Enemies))
        {
            builder.Append(actor.InstanceId).Append(':')
                .Append(actor.CurrentHp).Append(':')
                .Append(actor.Block.Sum).Append(':')
                .AppendJoin(',', actor.AttackSingle.HitAmounts).Append(':')
                .AppendJoin(',', actor.AttackRandom.HitAmounts).Append(':')
                .AppendJoin(',', actor.AttackAll.HitAmounts).Append(':');
            foreach (var status in actor.Statuses.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                builder.Append(status.Key).Append('=').Append(status.Value).Append(',');
            builder.Append('|');
        }

        foreach (var participant in BattleParticipantAccess.EffectiveParticipants(state))
        {
            builder.Append(participant.ParticipantId).Append(':')
                .Append(participant.Energy).Append(':')
                .Append(participant.EnergyPerTurn).Append(':')
                .Append(participant.DrawPerTurn).Append(':')
                .Append(participant.TargetAllyIndex).Append(':')
                .Append(participant.TargetEnemyIndex).Append(':')
                .Append(participant.ComboCount).Append(':')
                .Append(participant.MaxComboThisTurn).Append(':')
                .Append(participant.ComboGauge).Append(':')
                .Append(participant.LastPlayedOrigCost).Append(':')
                .Append(participant.NextCardComboFreePass).Append(':')
                .Append(participant.WildUsedInCurrentCombo).Append(':')
                .Append(participant.CardUseLockedThisTurn).Append(':');
            AppendCards(builder, participant.Hand);
            AppendCards(builder, participant.DrawPile);
            AppendCards(builder, participant.DiscardPile);
            AppendCards(builder, participant.ExhaustPile);
            AppendCards(builder, participant.SummonHeld);
            AppendCards(builder, participant.PowerCards);
            foreach (string potion in participant.Potions)
                builder.Append("potion=").Append(potion).Append(',');
            foreach (string relic in participant.OwnedRelicIds)
                builder.Append("relic=").Append(relic).Append(',');
            foreach (var usage in participant.CardUsesThisTurn
                         .OrderBy(pair => pair.Key, StringComparer.Ordinal))
                builder.Append(usage.Key).Append('=').Append(usage.Value).Append(',');
            foreach (var usage in participant.CharacterSkillUsesThisTurn
                         .OrderBy(pair => pair.Key, StringComparer.Ordinal))
                builder.Append("skill:").Append(usage.Key).Append('=')
                    .Append(usage.Value).Append(',');
            foreach (var usage in participant.PotionUsesThisTurn
                         .OrderBy(pair => pair.Key, StringComparer.Ordinal))
                builder.Append("potion:").Append(usage.Key).Append('=')
                    .Append(usage.Value).Append(',');
            builder.Append('|');
        }

        builder.Append("focus=").Append(participantId);
        return builder.ToString();
    }

    private static void AppendCards(
        StringBuilder builder,
        ImmutableArray<BattleCardInstance> cards)
    {
        builder.Append('[');
        foreach (var card in cards)
        {
            builder.Append(card.InstanceId).Append('/')
                .Append(card.CardDefinitionId).Append('/')
                .Append(card.IsUpgraded).Append('/')
                .Append(card.CostOverride).Append('/')
                .Append(card.PoolClassOverride).Append('/');
            foreach (var modifier in card.EffectiveCostModifiers)
                builder.Append(modifier.Amount).Append('@')
                    .Append((int)modifier.Duration).Append('@')
                    .Append(modifier.SourceId).Append(',');
            builder.Append(';');
        }
        builder.Append(']');
    }
}

public sealed record BattleCpuPlanningResult(
    BattleState State,
    ImmutableArray<CpuCommittedAction> Actions,
    ImmutableArray<CpuPlanningStop> Stops,
    ImmutableArray<string> InitializedParticipantIds)
{
    /// <summary>
    /// Exact state from which the first planning timeline item was committed.
    /// It includes participant initialization and Ready participants reopened
    /// by the external state change that triggered this planning pass.
    /// </summary>
    public BattleState? StateBeforePlanning { get; init; }

    public ImmutableArray<string> ReopenedParticipantIds { get; init; } =
        ImmutableArray<string>.Empty;
}

internal sealed record ReopenedCpuParticipants(
    BattleState State,
    ImmutableArray<string> ParticipantIds);

/// <summary>A journal-ready description of one fully completed CPU card play.</summary>
public sealed record CpuCommittedAction(
    string ParticipantId,
    string ActorInstanceId,
    string CardInstanceId,
    string CardDefinitionId,
    int PaidEnergy,
    int? TargetEnemyIndex,
    int? TargetAllyIndex,
    string DebugReason,
    ImmutableArray<BattleEvent> Events)
{
    /// <summary>The battle turn on which this decision was committed.</summary>
    public int Turn { get; init; }
    public BattleCardInstance? CardSnapshot { get; init; }
    public CardCostBreakdown? CostBreakdown { get; init; }
    public int UsesBefore { get; init; }
    public long RevisionBefore { get; init; }
    public long RevisionAfter { get; init; }
    public ImmutableArray<string> TargetActorInstanceIds { get; init; } = [];
    public ImmutableArray<string> SelectedInstanceIds { get; init; } = [];
    public CpuCommittedIntentDelta IntentDelta { get; init; } =
        CpuCommittedIntentDelta.Empty;
    public CpuCommittedActorContext ActorContext { get; init; } = new(0, 0, 0);

    /// <summary>
    /// Exact decision-time states used only by the Server replay projection.
    /// They are observational metadata and are never fed back into game rules.
    /// </summary>
    public BattleState? StateBefore { get; init; }
    public BattleState? StateAfter { get; init; }
    public ImmutableArray<string> ReopenedParticipantIds { get; init; } = [];

    /// <summary>
    /// Monotonic position in the combined action/Ready-stop planning timeline.
    /// A negative value identifies legacy/manual results without ordering metadata.
    /// </summary>
    public long PlanningOrdinal { get; init; } = -1;
}

public sealed record CpuCommittedIntentDelta(
    ImmutableArray<AttackHit> AddedSingleHits,
    ImmutableArray<AttackHit> AddedRandomHits,
    ImmutableArray<AttackHit> AddedAllHits,
    int BlockSumDelta,
    int BlockAddCountDelta)
{
    public static CpuCommittedIntentDelta Empty { get; } = new([], [], [], 0, 0);
}

public sealed record CpuCommittedActorContext(
    int Strength,
    int Weak,
    int Dexterity)
{
    public int AttackPower { get; init; }
    public int DefensePower { get; init; }
}

public sealed record CpuPlanningStop(
    string ParticipantId,
    CpuPlanningStopReason Reason,
    int PlaysCommitted)
{
    /// <summary>Monotonic position shared with <see cref="CpuCommittedAction"/>.</summary>
    public long PlanningOrdinal { get; init; } = -1;

    /// <summary>Exact states immediately before and after this CPU became Ready.</summary>
    public BattleState? StateBefore { get; init; }
    public BattleState? StateAfter { get; init; }
}

public enum CpuPlanningStopReason
{
    NoLegalAction = 0,
    ActorUnavailable = 1,
    PerActorPlayCap = 2,
    TotalPlayCap = 3,
    RepeatedState = 4,
}
