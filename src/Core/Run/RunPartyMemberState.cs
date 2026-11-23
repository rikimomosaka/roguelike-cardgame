using System.Collections.Immutable;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Events;
using RoguelikeCardGame.Core.Merchant;
using RoguelikeCardGame.Core.Rewards;

namespace RoguelikeCardGame.Core.Run;

/// <summary>
/// The persistent, member-owned part of a run. Map progress, encounters and battle
/// resolution remain on <see cref="RunState"/> because they are shared by the party.
/// </summary>
public sealed record RunPartyMemberState(
    string MemberId,
    int SlotIndex,
    string CharacterId,
    RunMemberControlMode ControlMode,
    int CurrentHp,
    int MaxHp,
    int Gold,
    ImmutableArray<CardInstance> Deck,
    ImmutableArray<string> Potions,
    int PotionSlotCount,
    ImmutableArray<string> Relics,
    RewardRngState RewardRngState,
    RewardState? ActiveReward,
    MerchantInventory? ActiveMerchant,
    EventInstance? ActiveEvent,
    bool ActiveRestPending,
    bool ActiveRestCompleted,
    ActStartRelicChoice? ActiveActStartRelicChoice,
    ImmutableArray<string> SeenCardBaseIds,
    ImmutableArray<string> AcquiredRelicIds,
    ImmutableArray<string> AcquiredPotionIds,
    int DiscardUsesSoFar = 0);

/// <summary>
/// Preferred controller outside the shared resolution phase. This is deliberately
/// independent from account ownership; network authorization belongs to Server.
/// </summary>
public enum RunMemberControlMode
{
    Human,
    Cpu,
}
