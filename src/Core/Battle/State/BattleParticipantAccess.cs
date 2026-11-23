using System;
using System.Collections.Immutable;
using System.Linq;

namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// Compatibility boundary for participant-owned card state.  New battle
/// snapshots persist <see cref="BattleState.Participants"/>.  Legacy snapshots
/// and tests that omit it are exposed as one synthesized solo participant.
/// </summary>
public static class BattleParticipantAccess
{
    public const string DefaultPrimaryParticipantId = "player:0";

    public static ImmutableArray<BattleParticipantState> EffectiveParticipants(
        BattleState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return !state.Participants.IsDefaultOrEmpty
            ? state.Participants
            : ImmutableArray.Create(CreatePrimaryFromRoot(state));
    }

    /// <summary>
    /// Returns participants in battlefield actor-slot order.  Participant
    /// insertion order is deliberately not a turn-order contract: a dead
    /// summon can leave slot 1 and a later summon can reuse that slot while
    /// being appended to the participant collection.
    /// </summary>
    public static ImmutableArray<BattleParticipantState> InActorSlotOrder(
        BattleState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return EffectiveParticipants(state)
            .Select((participant, index) => new
            {
                Participant = participant,
                OriginalIndex = index,
                SlotIndex = state.Allies.FirstOrDefault(actor =>
                    string.Equals(
                        actor.InstanceId,
                        participant.ActorInstanceId,
                        StringComparison.Ordinal))?.SlotIndex ?? int.MaxValue,
            })
            .OrderBy(item => item.SlotIndex)
            .ThenBy(item => item.OriginalIndex)
            .Select(item => item.Participant)
            .ToImmutableArray();
    }

    public static BattleParticipantState GetPrimary(BattleState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return !state.Participants.IsDefaultOrEmpty
            ? state.Participants[0]
            : CreatePrimaryFromRoot(state);
    }

    public static bool TryGet(
        BattleState state,
        string participantId,
        out BattleParticipantState participant)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(participantId))
        {
            participant = null!;
            return false;
        }

        foreach (var candidate in EffectiveParticipants(state))
        {
            if (string.Equals(
                    candidate.ParticipantId,
                    participantId,
                    StringComparison.Ordinal))
            {
                participant = candidate;
                return true;
            }
        }

        participant = null!;
        return false;
    }

    /// <summary>カード制御の敵参加者（<see cref="BattleState.EnemyParticipants"/>）を ID で探す。</summary>
    public static bool TryGetEnemy(
        BattleState state,
        string participantId,
        out BattleParticipantState participant)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!state.EnemyParticipants.IsDefaultOrEmpty)
        {
            foreach (var candidate in state.EnemyParticipants)
            {
                if (string.Equals(candidate.ParticipantId, participantId, StringComparison.Ordinal))
                {
                    participant = candidate;
                    return true;
                }
            }
        }
        participant = null!;
        return false;
    }

    public static bool TryGetByActor(
        BattleState state,
        string actorInstanceId,
        out BattleParticipantState participant)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(actorInstanceId))
        {
            participant = null!;
            return false;
        }

        foreach (var candidate in EffectiveParticipants(state))
        {
            if (string.Equals(
                    candidate.ActorInstanceId,
                    actorInstanceId,
                    StringComparison.Ordinal))
            {
                participant = candidate;
                return true;
            }
        }

        participant = null!;
        return false;
    }

    /// <summary>
    /// Promotes a legacy solo snapshot to an explicit primary participant.
    /// The root fields remain a compatibility mirror until DTO consumers move
    /// to Participants.
    /// </summary>
    public static BattleState InitializePrimary(
        BattleState state,
        string participantId = DefaultPrimaryParticipantId,
        BattleParticipantControllerKind controllerKind =
            BattleParticipantControllerKind.Human)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!state.Participants.IsDefaultOrEmpty)
            throw new InvalidOperationException(
                "Cannot initialize a primary participant when Participants is already populated.");

        var participant = CreatePrimaryFromRoot(
            state, participantId, controllerKind);
        return Replace(state, participant);
    }

    /// <summary>
    /// Replaces one participant.  Index zero is the compatibility primary and
    /// is the only participant mirrored to the legacy BattleState root fields.
    /// A legacy state is promoted when its synthesized primary is first replaced.
    /// </summary>
    public static BattleState Replace(
        BattleState state,
        BattleParticipantState participant)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(participant);
        ValidateIdentity(participant);

        if (state.Participants.IsDefaultOrEmpty)
        {
            return MirrorPrimary(
                state with
                {
                    Participants = ImmutableArray.Create(participant),
                },
                participant);
        }

        int index = FindParticipantIndex(state.Participants, participant.ParticipantId);
        if (index < 0)
            throw new InvalidOperationException(
                $"Battle participant '{participant.ParticipantId}' does not exist.");

        EnsureActorIdentityIsUnique(state.Participants, participant, index);
        var replaced = state with
        {
            Participants = state.Participants.SetItem(index, participant),
        };
        return index == 0 ? MirrorPrimary(replaced, participant) : replaced;
    }

    /// <summary>Adds an independently controlled participant in stable turn order.</summary>
    public static BattleState Add(
        BattleState state,
        BattleParticipantState participant)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(participant);
        ValidateIdentity(participant);

        if (state.Participants.IsDefaultOrEmpty)
            return Replace(state, participant);

        if (FindParticipantIndex(state.Participants, participant.ParticipantId) >= 0)
            throw new InvalidOperationException(
                $"Battle participant '{participant.ParticipantId}' already exists.");
        EnsureActorIdentityIsUnique(state.Participants, participant, ignoredIndex: -1);
        return state with { Participants = state.Participants.Add(participant) };
    }

    /// <summary>
    /// Temporarily projects one participant's independently owned resources to
    /// the legacy root fields so an existing solo battle processor can operate
    /// on that participant. The Participants collection is left unchanged.
    /// Call <see cref="CommitRootToParticipant"/> after the processor returns.
    /// </summary>
    public static BattleState ProjectToRoot(
        BattleState state,
        string participantId)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!TryGet(state, participantId, out var participant))
            throw new InvalidOperationException(
                $"Battle participant '{participantId}' does not exist.");

        return MirrorPrimary(state, participant);
    }

    /// <summary>
    /// Captures participant-owned resource fields changed by a legacy root
    /// processor, then restores the root compatibility mirror to participant
    /// index zero. Global battle fields and actor changes are preserved.
    /// </summary>
    public static BattleState CommitRootToParticipant(
        BattleState state,
        string participantId)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!TryGet(state, participantId, out var participant))
            throw new InvalidOperationException(
                $"Battle participant '{participantId}' does not exist.");

        var updated = CopyRootResources(state, participant);
        var committed = Replace(state, updated);
        return MirrorPrimary(committed, committed.Participants[0]);
    }

    /// <summary>
    /// 敵参加者を表示用に root へ投影する（コスト・コンボ等の表示計算用、state 本体は不変）。
    /// </summary>
    public static BattleState ProjectEnemyToRoot(
        BattleState state,
        string participantId)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!TryGetEnemy(state, participantId, out var participant))
            throw new InvalidOperationException(
                $"Enemy participant '{participantId}' does not exist.");
        return MirrorPrimary(state, participant);
    }

    /// <summary>
    /// <see cref="BattleState.Participants"/> に属さない参加者（敵参加者）を一時的に root へ投影する。
    /// 処理後は <see cref="CaptureRootResources"/> で取り出し、
    /// <see cref="RestoreRoot"/> で投影前の root へ戻す。
    /// </summary>
    internal static BattleState ProjectDetachedToRoot(
        BattleState state,
        BattleParticipantState participant)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(participant);
        return MirrorPrimary(state, participant);
    }

    /// <summary>root の参加者資源フィールドを指定参加者へ写し取る（state は変更しない）。</summary>
    internal static BattleParticipantState CaptureRootResources(
        BattleState state,
        BattleParticipantState participant)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(participant);
        return CopyRootResources(state, participant);
    }

    /// <summary>
    /// 投影前に <see cref="CaptureRootResources"/> で保存した root を書き戻す。
    /// actor・敵参加者など root 以外の変更は保持する。
    /// </summary>
    internal static BattleState RestoreRoot(
        BattleState state,
        BattleParticipantState savedRoot)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(savedRoot);
        return MirrorPrimary(state, savedRoot);
    }

    public static BattleParticipantState CreatePrimaryFromRoot(
        BattleState state,
        string participantId = DefaultPrimaryParticipantId,
        BattleParticipantControllerKind controllerKind =
            BattleParticipantControllerKind.Human)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(participantId))
            throw new ArgumentException(
                "Participant id must not be empty.", nameof(participantId));

        var actor = FindLegacyPrimaryActor(state)
            ?? throw new InvalidOperationException(
                "Cannot synthesize a battle participant without an allied actor.");

        return new BattleParticipantState(
            ParticipantId: participantId,
            ActorInstanceId: actor.InstanceId,
            ControllerKind: controllerKind,
            OwnerPlayerSlot: actor.OwnerPlayerSlot ?? actor.SlotIndex,
            CharacterId: state.CharacterId,
            TargetAllyIndex: state.TargetAllyIndex,
            TargetEnemyIndex: state.TargetEnemyIndex,
            Energy: state.Energy,
            EnergyPerTurn: state.EnergyMax,
            DrawPerTurn: state.DrawPerTurn,
            DrawPile: state.DrawPile,
            Hand: state.Hand,
            DiscardPile: state.DiscardPile,
            ExhaustPile: state.ExhaustPile,
            SummonHeld: state.SummonHeld,
            PowerCards: state.PowerCards,
            ComboCount: state.ComboCount,
            MaxComboThisTurn: state.MaxComboThisTurn,
            ComboGauge: state.ComboGauge,
            LastPlayedOrigCost: state.LastPlayedOrigCost,
            NextCardComboFreePass: state.NextCardComboFreePass,
            WildUsedInCurrentCombo: state.WildUsedInCurrentCombo,
            UsedCharacterSkillIdsThisTurn: state.UsedCharacterSkillIdsThisTurn,
            CharacterSkillUsesThisTurn: state.CharacterSkillUsesThisTurn,
            CardUsesThisTurn: state.CardUsesThisTurn,
            CardUseLockedThisTurn: state.CardUseLockedThisTurn,
            OwnedRelicIds: state.OwnedRelicIds,
            Potions: state.Potions,
            PotionUsesThisTurn: state.PotionUsesThisTurn,
            PendingCardPlay: state.PendingCardPlay,
            IsReady: false)
        {
            PlayedCardHistoryThisTurn = state.PlayedCardHistoryThisTurn,
            RepriseEligibleHistory = state.RepriseEligibleHistory,
            CurrentComboCardHistory = state.CurrentComboCardHistory,
            LastFinisherRecord = state.LastFinisherRecord,
            ArmedContingencies = state.ArmedContingencies,
            NextCardPlaySequence = state.NextCardPlaySequence,
            NextEffectResolutionSequence = state.NextEffectResolutionSequence,
            NextContingencyOrdinal = state.NextContingencyOrdinal,
            ComboCarryoverPending = state.ComboCarryoverPending,
            CharacterTraitUsesThisTurn = state.CharacterTraitUsesThisTurn,
        };
    }

    private static BattleState MirrorPrimary(
        BattleState state,
        BattleParticipantState primary) => state with
    {
        TargetAllyIndex = primary.TargetAllyIndex,
        TargetEnemyIndex = primary.TargetEnemyIndex,
        Energy = primary.Energy,
        EnergyMax = primary.EnergyPerTurn,
        DrawPerTurn = primary.DrawPerTurn,
        DrawPile = primary.DrawPile,
        Hand = primary.Hand,
        DiscardPile = primary.DiscardPile,
        ExhaustPile = primary.ExhaustPile,
        SummonHeld = primary.SummonHeld,
        PowerCards = primary.PowerCards,
        ComboCount = primary.ComboCount,
        MaxComboThisTurn = primary.MaxComboThisTurn,
        ComboGauge = primary.ComboGauge,
        LastPlayedOrigCost = primary.LastPlayedOrigCost,
        NextCardComboFreePass = primary.NextCardComboFreePass,
        WildUsedInCurrentCombo = primary.WildUsedInCurrentCombo,
        UsedCharacterSkillIdsThisTurn = primary.UsedCharacterSkillIdsThisTurn,
        CharacterSkillUsesThisTurn = primary.CharacterSkillUsesThisTurn,
        CardUsesThisTurn = primary.CardUsesThisTurn,
        CardUseLockedThisTurn = primary.CardUseLockedThisTurn,
        OwnedRelicIds = primary.OwnedRelicIds,
        Potions = primary.Potions,
        PotionUsesThisTurn = primary.PotionUsesThisTurn,
        PendingCardPlay = primary.PendingCardPlay,
        CharacterId = primary.CharacterId,
        PlayedCardHistoryThisTurn = primary.PlayedCardHistoryThisTurn,
        RepriseEligibleHistory = primary.RepriseEligibleHistory,
        CurrentComboCardHistory = primary.CurrentComboCardHistory,
        LastFinisherRecord = primary.LastFinisherRecord,
        ArmedContingencies = primary.ArmedContingencies,
        NextCardPlaySequence = primary.NextCardPlaySequence,
        NextEffectResolutionSequence = primary.NextEffectResolutionSequence,
        NextContingencyOrdinal = primary.NextContingencyOrdinal,
        ComboCarryoverPending = primary.ComboCarryoverPending,
        CharacterTraitUsesThisTurn = primary.CharacterTraitUsesThisTurn,
    };

    private static BattleParticipantState CopyRootResources(
        BattleState state,
        BattleParticipantState participant) => participant with
    {
        TargetAllyIndex = state.TargetAllyIndex,
        TargetEnemyIndex = state.TargetEnemyIndex,
        Energy = state.Energy,
        EnergyPerTurn = state.EnergyMax,
        DrawPerTurn = state.DrawPerTurn,
        DrawPile = state.DrawPile,
        Hand = state.Hand,
        DiscardPile = state.DiscardPile,
        ExhaustPile = state.ExhaustPile,
        SummonHeld = state.SummonHeld,
        PowerCards = state.PowerCards,
        ComboCount = state.ComboCount,
        MaxComboThisTurn = state.MaxComboThisTurn,
        ComboGauge = state.ComboGauge,
        LastPlayedOrigCost = state.LastPlayedOrigCost,
        NextCardComboFreePass = state.NextCardComboFreePass,
        WildUsedInCurrentCombo = state.WildUsedInCurrentCombo,
        UsedCharacterSkillIdsThisTurn = state.UsedCharacterSkillIdsThisTurn,
        CharacterSkillUsesThisTurn = state.CharacterSkillUsesThisTurn,
        CardUsesThisTurn = state.CardUsesThisTurn,
        CardUseLockedThisTurn = state.CardUseLockedThisTurn,
        OwnedRelicIds = state.OwnedRelicIds,
        Potions = state.Potions,
        PotionUsesThisTurn = state.PotionUsesThisTurn,
        PendingCardPlay = state.PendingCardPlay,
        CharacterId = state.CharacterId,
        PlayedCardHistoryThisTurn = state.PlayedCardHistoryThisTurn,
        RepriseEligibleHistory = state.RepriseEligibleHistory,
        CurrentComboCardHistory = state.CurrentComboCardHistory,
        LastFinisherRecord = state.LastFinisherRecord,
        ArmedContingencies = state.ArmedContingencies,
        NextCardPlaySequence = state.NextCardPlaySequence,
        NextEffectResolutionSequence = state.NextEffectResolutionSequence,
        NextContingencyOrdinal = state.NextContingencyOrdinal,
        ComboCarryoverPending = state.ComboCarryoverPending,
        CharacterTraitUsesThisTurn = state.CharacterTraitUsesThisTurn,
    };

    private static CombatActor? FindLegacyPrimaryActor(BattleState state) =>
        state.Allies
            .Where(actor => actor.DefinitionId == "hero")
            .OrderBy(actor => actor.SlotIndex)
            .FirstOrDefault()
        ?? state.Allies.OrderBy(actor => actor.SlotIndex).FirstOrDefault();

    private static int FindParticipantIndex(
        ImmutableArray<BattleParticipantState> participants,
        string participantId)
    {
        for (int i = 0; i < participants.Length; i++)
        {
            if (string.Equals(
                    participants[i].ParticipantId,
                    participantId,
                    StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private static void ValidateIdentity(BattleParticipantState participant)
    {
        if (string.IsNullOrWhiteSpace(participant.ParticipantId))
            throw new InvalidOperationException(
                "Battle participant id must not be empty.");
        if (string.IsNullOrWhiteSpace(participant.ActorInstanceId))
            throw new InvalidOperationException(
                "Battle participant actor instance id must not be empty.");
    }

    private static void EnsureActorIdentityIsUnique(
        ImmutableArray<BattleParticipantState> participants,
        BattleParticipantState participant,
        int ignoredIndex)
    {
        for (int i = 0; i < participants.Length; i++)
        {
            if (i == ignoredIndex) continue;
            if (string.Equals(
                    participants[i].ActorInstanceId,
                    participant.ActorInstanceId,
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Actor '{participant.ActorInstanceId}' already has a battle participant.");
        }
    }
}
