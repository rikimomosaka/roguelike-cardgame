using System;
using System.Collections.Immutable;
using System.Linq;

namespace RoguelikeCardGame.Core.Run;

/// <summary>
/// Compatibility bridge while legacy non-battle actions still operate on the root
/// CharacterId/HP/Gold/Deck fields. The root fields mirror the host member.
/// </summary>
public static class RunPartyAccess
{
    public const string DefaultHostMemberId = "player:0";

    public static string EffectiveHostMemberId(RunState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!string.IsNullOrWhiteSpace(state.HostMemberId))
            return state.HostMemberId;
        if (!state.PartyMembers.IsDefaultOrEmpty)
            return state.PartyMembers[0].MemberId;
        return DefaultHostMemberId;
    }

    public static ImmutableArray<RunPartyMemberState> EffectiveMembers(RunState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!state.PartyMembers.IsDefaultOrEmpty)
            return state.PartyMembers;

        return ImmutableArray.Create(FromLegacyRoot(
            state,
            EffectiveHostMemberId(state),
            slotIndex: 0,
            RunMemberControlMode.Human));
    }

    public static RunState EnsureInitialized(RunState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var members = EffectiveMembers(state);
        var hostId = EffectiveHostMemberId(state);
        return state with
        {
            HostMemberId = hostId,
            PartyMembers = members,
        };
    }

    /// <summary>
    /// Copies the current legacy root projection into the host member. Call this at
    /// persistence/DTO boundaries until every non-battle action is member-aware.
    /// </summary>
    public static RunState CaptureRootIntoHost(RunState state)
    {
        var initialized = EnsureInitialized(state);
        return CommitRootToMember(initialized, initialized.HostMemberId);
    }

    public static RunState ProjectMemberToRoot(RunState state, string memberId)
    {
        var initialized = EnsureInitialized(state);
        // The public projection contract starts from the host mirror. Preserve any
        // legacy root-only mutation before switching to a different member.
        var synchronized = string.Equals(
            memberId,
            initialized.HostMemberId,
            StringComparison.Ordinal)
            ? initialized
            : CommitRootToMember(initialized, initialized.HostMemberId);
        var member = FindRequired(synchronized.PartyMembers, memberId);
        return synchronized with
        {
            CharacterId = member.CharacterId,
            CurrentHp = member.CurrentHp,
            MaxHp = member.MaxHp,
            Gold = member.Gold,
            Deck = member.Deck,
            Potions = member.Potions,
            PotionSlotCount = member.PotionSlotCount,
            Relics = member.Relics,
            RewardRngState = member.RewardRngState,
            ActiveReward = member.ActiveReward,
            ActiveMerchant = member.ActiveMerchant,
            ActiveEvent = member.ActiveEvent,
            ActiveRestPending = member.ActiveRestPending,
            ActiveRestCompleted = member.ActiveRestCompleted,
            ActiveActStartRelicChoice = member.ActiveActStartRelicChoice,
            SeenCardBaseIds = member.SeenCardBaseIds,
            AcquiredRelicIds = member.AcquiredRelicIds,
            AcquiredPotionIds = member.AcquiredPotionIds,
            DiscardUsesSoFar = member.DiscardUsesSoFar,
        };
    }

    public static RunState CommitRootToMember(RunState state, string memberId)
    {
        var initialized = EnsureInitialized(state);
        var index = -1;
        for (var i = 0; i < initialized.PartyMembers.Length; i++)
        {
            if (string.Equals(initialized.PartyMembers[i].MemberId, memberId, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }
        if (index < 0)
            throw new InvalidOperationException($"Run party member '{memberId}' was not found.");

        var previous = initialized.PartyMembers[index];
        var captured = FromLegacyRoot(
            initialized,
            previous.MemberId,
            previous.SlotIndex,
            previous.ControlMode);
        var committed = initialized with
        {
            PartyMembers = initialized.PartyMembers.SetItem(index, captured),
        };
        return string.Equals(memberId, initialized.HostMemberId, StringComparison.Ordinal)
            ? committed
            : ProjectMemberToRoot(committed, initialized.HostMemberId);
    }

    public static RunPartyMemberState FromLegacyRoot(
        RunState state,
        string memberId,
        int slotIndex,
        RunMemberControlMode controlMode) =>
        new(
            MemberId: memberId,
            SlotIndex: slotIndex,
            CharacterId: state.CharacterId,
            ControlMode: controlMode,
            CurrentHp: state.CurrentHp,
            MaxHp: state.MaxHp,
            Gold: state.Gold,
            Deck: state.Deck,
            Potions: state.Potions,
            PotionSlotCount: state.PotionSlotCount,
            Relics: ImmutableArray.CreateRange(state.Relics),
            RewardRngState: state.RewardRngState,
            ActiveReward: state.ActiveReward,
            ActiveMerchant: state.ActiveMerchant,
            ActiveEvent: state.ActiveEvent,
            ActiveRestPending: state.ActiveRestPending,
            ActiveRestCompleted: state.ActiveRestCompleted,
            ActiveActStartRelicChoice: state.ActiveActStartRelicChoice,
            SeenCardBaseIds: state.SeenCardBaseIds,
            AcquiredRelicIds: state.AcquiredRelicIds,
            AcquiredPotionIds: state.AcquiredPotionIds,
            DiscardUsesSoFar: state.DiscardUsesSoFar);

    private static RunPartyMemberState FindRequired(
        ImmutableArray<RunPartyMemberState> members,
        string memberId) =>
        members.FirstOrDefault(member =>
            string.Equals(member.MemberId, memberId, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"Run party member '{memberId}' was not found.");
}
