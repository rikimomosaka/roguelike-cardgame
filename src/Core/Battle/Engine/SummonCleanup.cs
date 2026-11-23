using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// 死亡した召喚 actor (Side==Ally && DefinitionId != "hero" && !IsAlive)
/// の AssociatedSummonHeldInstanceId を辿り、対応カードを SummonHeld → DiscardPile に移動する。
/// 親 spec §5-4 / Phase 10.2.D spec §4-4 参照。
///
/// 呼出箇所: PlayerAttackingResolver / EnemyAttackingResolver / TurnStartProcessor (poison tick 後 / Lifetime tick 後)。
/// memory feedback ルール「state.Allies/Enemies 書き戻しは InstanceId 検索」準拠。
/// </summary>
internal static class SummonCleanup
{
    public static BattleState Apply(
        BattleState state, List<BattleEvent> events, ref int order)
    {
        var s = state;
        // TargetAllyIndex is an array position, not a battlefield SlotIndex.
        // Preserve actor identity before removing corpses so an index shift
        // cannot silently retarget a participant to a different ally.
        var rootTargetActorId = TargetActorInstanceId(
            s.Allies, s.TargetAllyIndex);
        var participantTargetActorIds = s.Participants.IsDefaultOrEmpty
            ? ImmutableDictionary<string, string?>.Empty
            : s.Participants.ToImmutableDictionary(
                participant => participant.ParticipantId,
                participant => TargetActorInstanceId(
                    s.Allies, participant.TargetAllyIndex));
        var deadSummons = s.Allies
            .Where(a => a.Side == ActorSide.Ally
                     && a.DefinitionId != "hero"
                     && !a.IsAlive
                     && (a.AssociatedSummonHeldInstanceId is not null
                         || a.RemainingLifetimeTurns is not null
                         || a.OwnerActorInstanceId is not null))
            .Select(a => (a.InstanceId, a.AssociatedSummonHeldInstanceId))
            .ToList();

        foreach (var (allyId, cardInstId) in deadSummons)
        {
            if (cardInstId is not null
                && !HasLivingAssociatedSummon(s, cardInstId, allyId)
                && TryMoveHeldCardToOwnerDiscard(s, cardInstId, out var moved))
            {
                s = moved;
            }

            // ally の AssociatedSummonHeldInstanceId を null 化（再処理防止）
            int allyIdx = -1;
            for (int i = 0; i < s.Allies.Length; i++)
            {
                if (s.Allies[i].InstanceId == allyId) { allyIdx = i; break; }
            }
            if (allyIdx >= 0)
            {
                var actor = s.Allies[allyIdx];
                s = s with { Allies = s.Allies.SetItem(
                    allyIdx, actor with { AssociatedSummonHeldInstanceId = null }) };
            }
            // ActorDeath is already in the event stream. Remove the corpse
            // and its independently controlled participant so a later summon
            // can reuse this battlefield slot. Replay keeps actor identity in
            // the immutable action journal.
            s = s with
            {
                Allies = s.Allies
                    .Where(actor => !string.Equals(
                        actor.InstanceId, allyId,
                        System.StringComparison.Ordinal))
                    .ToImmutableArray(),
                Participants = s.Participants.IsDefaultOrEmpty
                    ? s.Participants
                    : s.Participants
                        .Where(participant => !string.Equals(
                            participant.ActorInstanceId, allyId,
                            System.StringComparison.Ordinal))
                        .ToImmutableArray(),
            };
            // event 発火なし（state diff から UI が認識）
        }
        if (deadSummons.Count == 0)
            return s;

        int? fallbackIndex = FindFallbackAllyIndex(s.Allies);
        if (s.Participants.IsDefaultOrEmpty)
        {
            return s with
            {
                TargetAllyIndex = RemapTargetIndex(
                    s.Allies, rootTargetActorId, fallbackIndex),
            };
        }

        var remappedParticipants = s.Participants
            .Select(participant => participant with
            {
                TargetAllyIndex = RemapTargetIndex(
                    s.Allies,
                    participantTargetActorIds.GetValueOrDefault(
                        participant.ParticipantId),
                    fallbackIndex),
            })
            .ToImmutableArray();

        // Participant zero owns the compatibility root mirror. Use its
        // remapped target whenever explicit participants are present.
        return s with
        {
            Participants = remappedParticipants,
            TargetAllyIndex = remappedParticipants[0].TargetAllyIndex,
        };
    }

    /// <summary>
    /// One Unit card may directly create multiple actors. They form one
    /// ownership group and share the held-card instance id, so the card stays
    /// held until the final living member leaves play.
    /// </summary>
    private static bool HasLivingAssociatedSummon(
        BattleState state,
        string cardInstanceId,
        string excludingActorInstanceId)
    {
        return state.Allies.Any(actor =>
            actor.IsAlive
            && !string.Equals(
                actor.InstanceId,
                excludingActorInstanceId,
                System.StringComparison.Ordinal)
            && string.Equals(
                actor.AssociatedSummonHeldInstanceId,
                cardInstanceId,
                System.StringComparison.Ordinal));
    }

    private static string? TargetActorInstanceId(
        ImmutableArray<CombatActor> allies,
        int? targetIndex)
    {
        if (targetIndex is not { } index || index < 0 || index >= allies.Length)
            return null;

        return allies[index].InstanceId;
    }

    private static int? RemapTargetIndex(
        ImmutableArray<CombatActor> allies,
        string? targetActorInstanceId,
        int? fallbackIndex)
    {
        if (targetActorInstanceId is not null)
        {
            for (int index = 0; index < allies.Length; index++)
            {
                var actor = allies[index];
                if (actor.IsAlive && string.Equals(
                        actor.InstanceId,
                        targetActorInstanceId,
                        System.StringComparison.Ordinal))
                    return index;
            }
        }

        return fallbackIndex;
    }

    private static int? FindFallbackAllyIndex(
        ImmutableArray<CombatActor> allies)
    {
        int? selectedIndex = null;
        int selectedSlot = int.MaxValue;
        for (int index = 0; index < allies.Length; index++)
        {
            var actor = allies[index];
            if (!actor.IsAlive || actor.SlotIndex >= selectedSlot)
                continue;

            selectedIndex = index;
            selectedSlot = actor.SlotIndex;
        }

        return selectedIndex;
    }

    /// <summary>
    /// Summon-held cards belong to the participant who played them.  The root
    /// piles are only a compatibility mirror of participant zero, so searching
    /// root alone would strand cards summoned by a CPU or future human ally.
    /// </summary>
    private static bool TryMoveHeldCardToOwnerDiscard(
        BattleState state,
        string cardInstanceId,
        out BattleState result)
    {
        if (!state.Participants.IsDefaultOrEmpty)
        {
            foreach (var participant in state.Participants)
            {
                int index = IndexOf(participant.SummonHeld, cardInstanceId);
                if (index < 0) continue;

                var card = participant.SummonHeld[index];
                var updated = participant with
                {
                    SummonHeld = participant.SummonHeld.RemoveAt(index),
                    DiscardPile = participant.DiscardPile.Add(card),
                };
                result = BattleParticipantAccess.Replace(state, updated);
                if (string.Equals(
                        participant.ParticipantId,
                        state.Participants[0].ParticipantId,
                        System.StringComparison.Ordinal))
                {
                    result = BattleParticipantAccess.ProjectToRoot(
                        result, participant.ParticipantId);
                }
                return true;
            }
        }

        int legacyIndex = IndexOf(state.SummonHeld, cardInstanceId);
        if (legacyIndex < 0)
        {
            result = state;
            return false;
        }

        var legacyCard = state.SummonHeld[legacyIndex];
        result = state with
        {
            SummonHeld = state.SummonHeld.RemoveAt(legacyIndex),
            DiscardPile = state.DiscardPile.Add(legacyCard),
        };
        return true;
    }

    private static int IndexOf(
        System.Collections.Immutable.ImmutableArray<BattleCardInstance> cards,
        string instanceId)
    {
        for (int index = 0; index < cards.Length; index++)
            if (cards[index].InstanceId == instanceId) return index;
        return -1;
    }
}
