using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// Applies the card-zone/resource portion of a turn boundary to participants
/// other than the compatibility primary. Global combat ticks and actor-pool
/// resets remain owned by the existing TurnStart/TurnEnd processors and run
/// exactly once per battle turn.
/// </summary>
internal static class BattleParticipantTurnLifecycle
{
    public static BattleState CommitPrimaryRoot(BattleState state)
    {
        if (state.Participants.IsDefaultOrEmpty)
            return state;
        return BattleParticipantAccess.CommitRootToParticipant(
            state, state.Participants[0].ParticipantId);
    }

    public static BattleState ApplyEndToSecondaryParticipants(
        BattleState state,
        DataCatalog catalog)
    {
        if (state.Participants.IsDefaultOrEmpty)
            return state;

        string primaryParticipantId = state.Participants[0].ParticipantId;
        var participantIds = BattleParticipantAccess.InActorSlotOrder(state)
            .Where(participant => !string.Equals(
                participant.ParticipantId,
                primaryParticipantId,
                StringComparison.Ordinal))
            .Select(participant => participant.ParticipantId)
            .ToArray();
        var result = state;
        foreach (string participantId in participantIds)
        {
            if (!BattleParticipantAccess.TryGet(
                    result,
                    participantId,
                    out var participant))
                continue;
            var projected = BattleParticipantAccess.ProjectToRoot(result, participantId);
            var participantActor = result.Allies.FirstOrDefault(actor =>
                string.Equals(
                    actor.InstanceId,
                    participant.ActorInstanceId,
                    StringComparison.Ordinal));
            projected = ApplyTurnEndToProjectedRoot(
                projected, participantActor, catalog);
            result = BattleParticipantAccess.CommitRootToParticipant(
                projected, participantId);
            result = ResetCpuPlayCount(result, participantId);
        }

        return result;
    }

    /// <summary>
    /// root へ投影済みの参加者 1 人分のターン終了処理（コンボ・ターン限定状態のリセット、
    /// ターン終了まで有効なコスト変更の解除、待機以外の手札を捨札へ）。
    /// 味方の副参加者と敵参加者（<see cref="EnemyCardPlanner"/>）で共通利用する。
    /// </summary>
    internal static BattleState ApplyTurnEndToProjectedRoot(
        BattleState projected,
        CombatActor? participantActor,
        DataCatalog catalog)
    {
        projected = ComboTurnBoundaryRules.ResetAtTurnEnd(
            projected,
            participantActor);
        projected = BattleCardCostModifierLifecycle.Remove(
            projected, CardCostModifierDuration.UntilTurnEnd);

        var retained = ImmutableArray.CreateBuilder<BattleCardInstance>();
        var discard = projected.DiscardPile.ToBuilder();
        foreach (var card in projected.Hand)
        {
            if (!catalog.TryGetCard(card.CardDefinitionId, out var definition))
            {
                discard.Add(card);
                continue;
            }

            if (BattleCardRetention.ShouldRetain(card, definition))
                retained.Add(card);
            else
                discard.Add(card);
        }

        return projected with
        {
            Hand = retained.ToImmutable(),
            DiscardPile = discard.ToImmutable(),
        };
    }

    public static BattleState ApplyStartToAllParticipants(
        BattleState state,
        IRng rng,
        DataCatalog catalog) => ApplyStartToAllParticipants(
            state, rng, catalog, out _);

    public static BattleState ApplyStartToAllParticipants(
        BattleState state,
        IRng rng,
        DataCatalog catalog,
        out IReadOnlyList<BattleEvent> shuffleEvents)
    {
        var events = new List<BattleEvent>();
        if (state.Participants.IsDefaultOrEmpty)
        {
            var owner = ProvisionalSoloHeroOwnerResolver.Resolve(state)?.Hero;
            var next = DrawHelper.Draw(
                CharacterTraitRuntime.ResetTurnUsage(state) with
                {
                    Energy = EnergyMath.AddSaturating(
                        state.Energy, state.EnergyMax),
                },
                state.DrawPerTurn,
                rng,
                DrawHelper.HandCap,
                owner,
                out _,
                out int shuffledCardCount);
            if (shuffledCardCount > 0)
            {
                events.Add(new BattleEvent(
                    BattleEventKind.Shuffle,
                    Order: 0,
                    CasterInstanceId: owner?.InstanceId,
                    TargetInstanceId: owner?.InstanceId,
                    Amount: shuffledCardCount));
            }
            shuffleEvents = events;
            return next;
        }

        var participantIds = BattleParticipantAccess.InActorSlotOrder(state)
            .Select(participant => participant.ParticipantId)
            .ToArray();
        var result = state;
        foreach (string participantId in participantIds)
        {
            if (!BattleParticipantAccess.TryGet(result, participantId, out var participant))
                continue;
            var actor = result.Allies.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.InstanceId,
                    participant.ActorInstanceId,
                    StringComparison.Ordinal));
            if (actor is null || !actor.IsAlive)
                continue;

            var projected = BattleParticipantAccess.ProjectToRoot(result, participantId);
            projected = ComboTurnBoundaryRules.ResetAtTurnStart(projected);
            projected = CharacterTraitRuntime.ResetTurnUsage(projected);
            projected = BattleCardCostModifierLifecycle.Remove(
                projected, CardCostModifierDuration.UntilTurnEnd);
            projected = projected with
            {
                Energy = EnergyMath.AddSaturating(
                    projected.Energy, projected.EnergyMax),
            };

            int handLimit = catalog.TryGetUnit(participant.CharacterId, out var unit)
                ? unit.HandLimit
                : DrawHelper.HandCap;
            projected = DrawHelper.Draw(
                projected,
                projected.DrawPerTurn,
                rng,
                handLimit,
                actor,
                out _,
                out int shuffledCardCount);
            if (shuffledCardCount > 0)
            {
                events.Add(new BattleEvent(
                    BattleEventKind.Shuffle,
                    Order: events.Count,
                    CasterInstanceId: actor.InstanceId,
                    TargetInstanceId: actor.InstanceId,
                    Amount: shuffledCardCount));
            }
            result = BattleParticipantAccess.CommitRootToParticipant(
                projected, participantId);
            result = ResetCpuPlayCount(result, participantId);
        }

        shuffleEvents = events;
        return SetAllReady(result, isReady: false);
    }

    public static BattleState SetAllReady(BattleState state, bool isReady)
    {
        if (state.Participants.IsDefaultOrEmpty)
            return state;
        return state with
        {
            Participants = state.Participants
                .Select(participant => participant with { IsReady = isReady })
                .ToImmutableArray(),
        };
    }

    public static BattleState ResetCpuPlayCountsForAllParticipants(
        BattleState state)
    {
        if (state.CpuTotalCardPlaysThisTurn == 0
            && (state.Participants.IsDefaultOrEmpty
                || state.Participants.All(participant =>
                    participant.CpuCardPlaysThisTurn == 0
                    && participant.CpuLegalActionKeysWhenReadied.Count == 0)))
            return state;

        return state with
        {
            CpuTotalCardPlaysThisTurn = 0,
            Participants = state.Participants
                .Select(participant => participant with
                {
                    CpuCardPlaysThisTurn = 0,
                    CpuLegalActionKeysWhenReadied = ImmutableHashSet<string>.Empty,
                })
                .ToImmutableArray(),
        };
    }

    public static BattleState ApplyTerminalReset(BattleState state)
    {
        if (state.Participants.IsDefaultOrEmpty)
            return state with { CpuTotalCardPlaysThisTurn = 0 };

        var participantIds = BattleParticipantAccess.InActorSlotOrder(state)
            .Select(participant => participant.ParticipantId)
            .ToArray();
        var result = state;
        foreach (string participantId in participantIds)
        {
            var projected = BattleParticipantAccess.ProjectToRoot(result, participantId);
            projected = ComboTurnBoundaryRules.ResetWithoutCarryover(projected);
            projected = BattleCardCostModifierLifecycle.Remove(
                projected, CardCostModifierDuration.UntilTurnEnd);
            result = BattleParticipantAccess.CommitRootToParticipant(
                projected, participantId);
            result = ResetCpuPlayCount(result, participantId);
        }

        return SetAllReady(
            result with { CpuTotalCardPlaysThisTurn = 0 },
            isReady: true);
    }

    private static BattleState ResetCpuPlayCount(
        BattleState state,
        string participantId)
    {
        if (!BattleParticipantAccess.TryGet(state, participantId, out var participant)
            || (participant.CpuCardPlaysThisTurn == 0
                && participant.CpuLegalActionKeysWhenReadied.Count == 0))
            return state;
        return BattleParticipantAccess.Replace(
            state, participant with
            {
                CpuCardPlaysThisTurn = 0,
                CpuLegalActionKeysWhenReadied = ImmutableHashSet<string>.Empty,
            });
    }
}
