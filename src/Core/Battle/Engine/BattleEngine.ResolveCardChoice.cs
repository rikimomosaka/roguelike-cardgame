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
    /// <summary>
    /// Phase 10.5.M2-Choose: PendingCardPlay 状態から resume。
    /// 選択済 instance ids で choose effect を適用、残り effect を続行 (必要なら再 pause)、
    /// 完了したら FinalizeCardPlay (PlayCard と共通) を実行する。
    /// </summary>
    public static (BattleState, IReadOnlyList<BattleEvent>) ResolveCardChoice(
        BattleState state, ImmutableArray<string> selectedInstanceIds,
        IRng rng, DataCatalog catalog)
    {
        var pendingBefore = state.PendingCardPlay;
        if (!IsResolvingEffectChoice(state.PendingCardPlay))
            RejectIfParticipantReady(state, participantId: null, "ResolveCardChoice");
        var (next, events) = ResolveCardChoiceForActor(
            state, casterActorInstanceId: null,
            selectedInstanceIds, rng, catalog);
        if (!next.Participants.IsDefaultOrEmpty)
        {
            next = BattleParticipantAccess.CommitRootToParticipant(
                next, next.Participants[0].ParticipantId);
        }
        (next, events) = ContinueAfterEffectChoice(
            next, pendingBefore, events, rng, catalog);
        (next, events) = ApplyOutcomeAfterPlanningCommand(next, events);
        return (IncrementActionRevision(next), events);
    }

    /// <summary>Resolves the pending choice owned by one battle participant.</summary>
    public static (BattleState, IReadOnlyList<BattleEvent>) ResolveCardChoice(
        BattleState state, string participantId,
        ImmutableArray<string> selectedInstanceIds,
        IRng rng, DataCatalog catalog)
    {
        if (!BattleParticipantAccess.TryGet(state, participantId, out var participant))
            throw new InvalidOperationException(
                $"Battle participant '{participantId}' does not exist.");
        var pendingBefore = participant.PendingCardPlay;
        if (!IsResolvingEffectChoice(participant.PendingCardPlay))
            RejectIfParticipantReady(state, participantId, "ResolveCardChoice");

        var projected = BattleParticipantAccess.ProjectToRoot(state, participantId);
        var (processed, events) = ResolveCardChoiceForActor(
            projected, participant.ActorInstanceId,
            selectedInstanceIds, rng, catalog);
        var committed = BattleParticipantAccess.CommitRootToParticipant(
            processed, participantId);
        (committed, events) = ContinueAfterEffectChoice(
            committed, pendingBefore, events, rng, catalog);
        (committed, events) = ApplyOutcomeAfterPlanningCommand(
            committed, events);
        return (IncrementActionRevision(committed), events);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) ResolveCardChoiceForActor(
        BattleState state, string? casterActorInstanceId,
        ImmutableArray<string> selectedInstanceIds,
        IRng rng, DataCatalog catalog)
    {
        var pending = state.PendingCardPlay
            ?? throw new InvalidOperationException(
                "Cannot resolve card choice: no PendingCardPlay set");

        // validate selection (count + candidate membership + no duplicates)
        if (selectedInstanceIds.IsDefault)
            throw new InvalidOperationException("selectedInstanceIds must not be default");
        if (selectedInstanceIds.Length != pending.Choice.Count)
            throw new InvalidOperationException(
                $"Expected {pending.Choice.Count} selections, got {selectedInstanceIds.Length}");
        if (selectedInstanceIds.Distinct().Count() != selectedInstanceIds.Length)
            throw new InvalidOperationException("Duplicate selections not allowed");
        foreach (var id in selectedInstanceIds)
        {
            if (!pending.Choice.CandidateInstanceIds.Contains(id))
                throw new InvalidOperationException(
                    $"Selected '{id}' not in choice candidates");
        }

        if (pending.EffectExecution is { Kind: EffectExecutionKind.Afterimage })
        {
            return ResolveAfterimageChoice(
                state,
                pending,
                selectedInstanceIds,
                rng,
                catalog);
        }
        if (pending.EffectExecution is { Kind: EffectExecutionKind.Contingency })
        {
            return ResolveContingencyChoice(
                state,
                pending,
                selectedInstanceIds,
                rng,
                catalog);
        }

        // pending 中のカード取得 (まだ Hand に居る前提)
        var card = state.Hand.FirstOrDefault(c => c.InstanceId == pending.CardInstanceId)
            ?? throw new InvalidOperationException(
                $"PendingCardPlay card '{pending.CardInstanceId}' not found in Hand");
        if (!catalog.TryGetCard(card.CardDefinitionId, out var def))
            throw new InvalidOperationException(
                $"PendingCardPlay card def '{card.CardDefinitionId}' not in catalog");

        if (pending.EffectExecution is not null)
        {
            return ResolveEffectExecutionChoice(
                state,
                pending,
                card,
                def,
                selectedInstanceIds,
                rng,
                catalog);
        }

        var effects = (card.IsUpgraded && def.UpgradedEffects is not null)
            ? def.UpgradedEffects
            : def.Effects;
        var pendingEffect = effects[pending.EffectIndex];

        // pending クリア + choose effect 適用
        var s = state with { PendingCardPlay = null };
        CombatActor caster;
        if (casterActorInstanceId is null)
        {
            caster = ProvisionalSoloHeroOwnerResolver.Resolve(s)?.Hero
                ?? throw new InvalidOperationException(
                    "ResolveCardChoice requires a living hero owner");
        }
        else
        {
            caster = s.Allies[FindLivingParticipantCasterIndex(
                s, casterActorInstanceId)];
        }
        var events = new List<BattleEvent>();
        int order = 0;
        var source = new CombatSource(
            CombatSourceKind.CardEffect,
            OwnerSlot: caster.OwnerPlayerSlot ?? caster.SlotIndex,
            OwnerInstanceId: caster.OwnerActorInstanceId ?? caster.InstanceId,
            ActorInstanceId: caster.InstanceId,
            DefinitionId: card.CardDefinitionId,
            InstanceId: card.InstanceId,
            EffectIndex: pending.EffectIndex,
            Scope: pendingEffect.Scope.ToString().ToLowerInvariant());

        var (afterChose, evsChose) = EffectApplier.ApplyChoseEffect(
            s, caster, pendingEffect, selectedInstanceIds, rng, catalog, source,
            playingCardInstanceId: pending.CardInstanceId);
        s = afterChose;
        foreach (var ev in evsChose) { events.Add(ev with { Order = order++ }); }
        caster = FindParticipantCaster(s, caster.InstanceId) ?? caster;

        // 残り effect 続行 (再 pause 可能性あり)
        var (afterEffects, effectEvents, summonedActorInstanceIds, _) = ApplyEffectsFrom(
            s, caster, card, effects,
            startIndex: pending.EffectIndex + 1,
            newCombo: s.ComboCount,
            summonedActorInstanceIdsIn: pending.SummonedActorInstanceIdsBefore,
            ref order, rng, catalog);
        s = afterEffects;
        events.AddRange(effectEvents);

        // 再 pause していたら早期返却 (新 PendingCardPlay は ApplyEffectsFrom 内で書かれている)
        if (s.PendingCardPlay is not null)
            return (s, events);

        var (afterReprise, repriseEvents, reprisePaused) = ExecuteRepriseIfPresent(
            s,
            FindParticipantCaster(s, caster.InstanceId) ?? caster,
            card,
            def,
            summonedActorInstanceIds,
            ref order,
            rng,
            catalog);
        s = afterReprise;
        events.AddRange(repriseEvents);
        if (reprisePaused)
            return (s, events);

        // FinalizeCardPlay (PlayCard と共通)
        return FinalizeCardPlay(
            s, caster.InstanceId, card, def, summonedActorInstanceIds,
            events, ref order, rng, catalog);
    }

    private static bool IsResolvingEffectChoice(PendingCardPlay? pending) =>
        pending?.EffectExecution?.Kind is EffectExecutionKind.Afterimage
            or EffectExecutionKind.Contingency;

    private static (BattleState State, IReadOnlyList<BattleEvent> Events)
        ContinueAfterEffectChoice(
            BattleState state,
            PendingCardPlay? pendingBefore,
            IReadOnlyList<BattleEvent> priorEvents,
            IRng rng,
            DataCatalog catalog)
    {
        var s = state;
        var events = priorEvents.ToList();
        int order = events.Count;
        var deferredFinisherCard =
            pendingBefore?.EffectExecution?.PostContingencyFinisherCard;
        var deferredFinisherActorInstanceId =
            pendingBefore?.EffectExecution?.PostContingencyFinisherActorInstanceId;
        if (!HasAnyPendingChoice(s)
            && pendingBefore?.EffectExecution is
            {
                Kind: EffectExecutionKind.Contingency,
                TriggerPreviousCombo: int previousCombo,
                TriggerCurrentCombo: int currentCombo,
            } contingency)
        {
            var result = ContingencyResolutionEngine.ResolveComboReached(
                s,
                contingency.CasterActorInstanceId,
                previousCombo,
                currentCombo,
                rng,
                catalog,
                order);
            s = result.State;
            foreach (var battleEvent in result.Events)
                events.Add(battleEvent with { Order = order++ });

            if (result.PausedForChoice
                && deferredFinisherCard is not null
                && deferredFinisherActorInstanceId is not null)
            {
                s = UpdatePendingEffectExecutionForActor(
                    s,
                    deferredFinisherActorInstanceId,
                    execution => execution with
                    {
                        PostContingencyFinisherCard = deferredFinisherCard,
                        PostContingencyFinisherActorInstanceId =
                            deferredFinisherActorInstanceId,
                    });
            }
            else if (!result.PausedForChoice
                && deferredFinisherCard is not null
                && deferredFinisherActorInstanceId is not null)
            {
                if (!catalog.TryGetCard(
                        deferredFinisherCard.CardDefinitionId,
                        out var finisherDefinition))
                {
                    throw new InvalidOperationException(
                        $"Deferred Finisher card definition "
                        + $"'{deferredFinisherCard.CardDefinitionId}' does not exist.");
                }

                s = ApplyFinisherForParticipant(
                    s,
                    deferredFinisherActorInstanceId,
                    deferredFinisherCard,
                    finisherDefinition,
                    events,
                    ref order);
            }
        }

        if (!HasAnyPendingChoice(s) && s.PendingTurnResolution is not null)
        {
            var resumed = ResumeQueuedTurnAfterChoice(s, rng, catalog);
            s = resumed.State;
            foreach (var battleEvent in resumed.Events)
                events.Add(battleEvent with { Order = order++ });
        }

        return (s, events);
    }

    private static BattleState UpdatePendingEffectExecutionForActor(
        BattleState state,
        string actorInstanceId,
        Func<PendingEffectExecution, PendingEffectExecution> update)
    {
        if (state.Participants.IsDefaultOrEmpty)
        {
            var pending = state.PendingCardPlay
                ?? throw new InvalidOperationException(
                    "Contingency paused without a pending choice.");
            var execution = pending.EffectExecution
                ?? throw new InvalidOperationException(
                    "Contingency choice is missing its continuation.");
            return state with
            {
                PendingCardPlay = pending with
                {
                    EffectExecution = update(execution),
                },
            };
        }

        if (!BattleParticipantAccess.TryGetByActor(
                state, actorInstanceId, out var participant))
        {
            throw new InvalidOperationException(
                $"Battle participant for actor '{actorInstanceId}' does not exist.");
        }

        var projected = BattleParticipantAccess.ProjectToRoot(
            state, participant.ParticipantId);
        var projectedPending = projected.PendingCardPlay
            ?? throw new InvalidOperationException(
                "Contingency paused without a participant pending choice.");
        var projectedExecution = projectedPending.EffectExecution
            ?? throw new InvalidOperationException(
                "Contingency choice is missing its participant continuation.");
        projected = projected with
        {
            PendingCardPlay = projectedPending with
            {
                EffectExecution = update(projectedExecution),
            },
        };
        return BattleParticipantAccess.CommitRootToParticipant(
            projected, participant.ParticipantId);
    }

    private static BattleState ApplyFinisherForParticipant(
        BattleState state,
        string actorInstanceId,
        BattleCardInstance card,
        CardDefinition definition,
        List<BattleEvent> events,
        ref int order)
    {
        if (state.Participants.IsDefaultOrEmpty)
        {
            return ApplyFinisher(
                state, actorInstanceId, card, definition, events, ref order);
        }

        if (!BattleParticipantAccess.TryGetByActor(
                state, actorInstanceId, out var participant))
        {
            throw new InvalidOperationException(
                $"Battle participant for Finisher actor '{actorInstanceId}' does not exist.");
        }

        var projected = BattleParticipantAccess.ProjectToRoot(
            state, participant.ParticipantId);
        projected = ApplyFinisher(
            projected, actorInstanceId, card, definition, events, ref order);
        return BattleParticipantAccess.CommitRootToParticipant(
            projected, participant.ParticipantId);
    }

    private static bool HasAnyPendingChoice(BattleState state) =>
        state.PendingCardPlay is not null
        || (!state.Participants.IsDefaultOrEmpty
            && state.Participants.Any(participant =>
                participant.PendingCardPlay is not null));
}
