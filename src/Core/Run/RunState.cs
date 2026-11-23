using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Events;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Merchant;
using RoguelikeCardGame.Core.Rewards;

namespace RoguelikeCardGame.Core.Run;

/// <summary>ソロ／マルチ共通のラン 1 回分の状態。ソロのみ ISaveRepository で永続化される。</summary>
public sealed record RunState(
    int SchemaVersion,
    int CurrentAct,
    int CurrentNodeId,
    ImmutableArray<int> VisitedNodeIds,
    ImmutableDictionary<int, TileKind> UnknownResolutions,

    // --- Phase 5 additions ---
    string CharacterId,
    int CurrentHp,
    int MaxHp,
    int Gold,
    ImmutableArray<CardInstance> Deck,
    ImmutableArray<string> Potions,
    int PotionSlotCount,
    BattlePlaceholderState? ActiveBattle,
    RewardState? ActiveReward,
    ImmutableArray<string> EncounterQueueWeak,
    ImmutableArray<string> EncounterQueueStrong,
    ImmutableArray<string> EncounterQueueElite,
    ImmutableArray<string> EncounterQueueBoss,
    RewardRngState RewardRngState,

    // --- Phase 6 additions ---
    MerchantInventory? ActiveMerchant,
    EventInstance? ActiveEvent,
    bool ActiveRestPending,
    bool ActiveRestCompleted,

    // --- existing ---
    IReadOnlyList<string> Relics,
    long PlaySeconds,
    ulong RngSeed,
    DateTimeOffset SavedAtUtc,
    RunProgress Progress,
    string RunId,                                   // ← Phase 7
    ActStartRelicChoice? ActiveActStartRelicChoice, // ← Phase 7

    // --- Phase 8 additions (bestiary tracking) ---
    ImmutableArray<string> SeenCardBaseIds,
    ImmutableArray<string> AcquiredRelicIds,
    ImmutableArray<string> AcquiredPotionIds,
    ImmutableArray<string> EncounteredEnemyIds,

    // --- Multi-act run history (v7) ---
    // VisitedNodeIds はアクト遷移でクリアされるため、過去アクトのマス情報を残せない。
    // ActTransition.AdvanceAct が旧アクト分を集約して追記する。
    ImmutableArray<JourneyEntry> JourneyLog,

    int DiscardUsesSoFar = 0)
{
    /// <summary>JSON スキーマバージョン。v8 = パーティメンバー所有状態を追加。</summary>
    public const int CurrentSchemaVersion = 8;

    /// <summary>The representative member used by the legacy root projection.</summary>
    public string HostMemberId { get; init; } = string.Empty;

    /// <summary>Member-owned state. Map and battle progress remain shared on RunState.</summary>
    public ImmutableArray<RunPartyMemberState> PartyMembers { get; init; } =
        ImmutableArray<RunPartyMemberState>.Empty;

    public static RunState NewSoloRun(
        DataCatalog catalog,
        ulong rngSeed,
        int startNodeId,
        ImmutableDictionary<int, TileKind> unknownResolutions,
        ImmutableArray<string> encounterQueueWeak,
        ImmutableArray<string> encounterQueueStrong,
        ImmutableArray<string> encounterQueueElite,
        ImmutableArray<string> encounterQueueBoss,
        DateTimeOffset nowUtc,
        string characterId = "default",
        string? runId = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(unknownResolutions);

        if (!catalog.TryGetCharacter(characterId, out var ch))
            throw new InvalidOperationException($"Character \"{characterId}\" が DataCatalog に存在しません");

        foreach (var id in ch.Deck)
            if (!catalog.TryGetCard(id, out _))
                throw new InvalidOperationException(
                    $"Character \"{characterId}\" のデッキが参照するカード ID \"{id}\" が存在しません");

        var potionBuilder = ImmutableArray.CreateBuilder<string>(ch.PotionSlotCount);
        for (int i = 0; i < ch.PotionSlotCount; i++) potionBuilder.Add("");
        var potions = potionBuilder.ToImmutable();

        if (!catalog.RewardTables.TryGetValue("act1", out var rt))
            throw new InvalidOperationException("RewardTable \"act1\" が DataCatalog に存在しません");

        var deck = ImmutableArray.CreateRange(ch.Deck.Select(id => new CardInstance(id, false)));

        var state = new RunState(
            SchemaVersion: CurrentSchemaVersion,
            CurrentAct: 1,
            CurrentNodeId: startNodeId,
            VisitedNodeIds: ImmutableArray<int>.Empty,
            UnknownResolutions: unknownResolutions,
            CharacterId: characterId,
            CurrentHp: ch.MaxHp,
            MaxHp: ch.MaxHp,
            Gold: ch.StartingGold,
            Deck: deck,
            Potions: potions,
            PotionSlotCount: ch.PotionSlotCount,
            ActiveBattle: null,
            ActiveReward: null,
            EncounterQueueWeak: encounterQueueWeak,
            EncounterQueueStrong: encounterQueueStrong,
            EncounterQueueElite: encounterQueueElite,
            EncounterQueueBoss: encounterQueueBoss,
            RewardRngState: new RewardRngState(
                rt.PotionDynamic.InitialPercent, rt.EpicChance.InitialBonus),
            ActiveMerchant: null,
            ActiveEvent: null,
            ActiveRestPending: false,
            ActiveRestCompleted: false,
            Relics: Array.Empty<string>(),
            PlaySeconds: 0L,
            RngSeed: rngSeed,
            SavedAtUtc: nowUtc,
            Progress: RunProgress.InProgress,
            RunId: runId ?? Guid.NewGuid().ToString(),
            ActiveActStartRelicChoice: null,
            SeenCardBaseIds: ImmutableArray.CreateRange(ch.Deck.Distinct()),
            AcquiredRelicIds: ImmutableArray<string>.Empty,
            AcquiredPotionIds: ImmutableArray<string>.Empty,
            EncounteredEnemyIds: ImmutableArray<string>.Empty,
            JourneyLog: ImmutableArray<JourneyEntry>.Empty);

        return state with
        {
            HostMemberId = RunPartyAccess.DefaultHostMemberId,
            PartyMembers = ImmutableArray.Create(RunPartyAccess.FromLegacyRoot(
                state,
                RunPartyAccess.DefaultHostMemberId,
                slotIndex: 0,
                RunMemberControlMode.Human)),
        };
    }

    /// <summary>
    /// Creates the persistent party shell. During this migration phase the host member is
    /// also projected through the legacy root fields used by existing run actions.
    /// </summary>
    public static RunState NewPartyRun(
        DataCatalog catalog,
        IReadOnlyList<string> characterIds,
        ulong rngSeed,
        int startNodeId,
        ImmutableDictionary<int, TileKind> unknownResolutions,
        ImmutableArray<string> encounterQueueWeak,
        ImmutableArray<string> encounterQueueStrong,
        ImmutableArray<string> encounterQueueElite,
        ImmutableArray<string> encounterQueueBoss,
        DateTimeOffset nowUtc,
        string? runId = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(characterIds);
        if (characterIds.Count is < 1 or > 4)
            throw new ArgumentOutOfRangeException(
                nameof(characterIds), "A party must contain between 1 and 4 characters.");
        if (characterIds.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Character IDs must not be empty.", nameof(characterIds));
        if (characterIds.Distinct(StringComparer.Ordinal).Count() != characterIds.Count)
            throw new ArgumentException("A party cannot contain the same character twice.", nameof(characterIds));

        var state = NewSoloRun(
            catalog,
            rngSeed,
            startNodeId,
            unknownResolutions,
            encounterQueueWeak,
            encounterQueueStrong,
            encounterQueueElite,
            encounterQueueBoss,
            nowUtc,
            characterIds[0],
            runId);

        if (!catalog.RewardTables.TryGetValue("act1", out var rewardTable))
            throw new InvalidOperationException("RewardTable \"act1\" was not found in DataCatalog.");

        var members = ImmutableArray.CreateBuilder<RunPartyMemberState>(characterIds.Count);
        for (var slot = 0; slot < characterIds.Count; slot++)
        {
            var characterId = characterIds[slot];
            if (!catalog.TryGetCharacter(characterId, out var character))
                throw new InvalidOperationException($"Character \"{characterId}\" was not found in DataCatalog.");
            foreach (var cardId in character.Deck)
            {
                if (!catalog.TryGetCard(cardId, out _))
                    throw new InvalidOperationException(
                        $"Character \"{characterId}\" references unknown card \"{cardId}\".");
            }

            members.Add(new RunPartyMemberState(
                MemberId: $"player:{slot}",
                SlotIndex: slot,
                CharacterId: characterId,
                ControlMode: slot == 0 ? RunMemberControlMode.Human : RunMemberControlMode.Cpu,
                CurrentHp: character.MaxHp,
                MaxHp: character.MaxHp,
                Gold: character.StartingGold,
                Deck: ImmutableArray.CreateRange(
                    character.Deck.Select(id => new CardInstance(id, false))),
                Potions: ImmutableArray.CreateRange(
                    Enumerable.Repeat(string.Empty, character.PotionSlotCount)),
                PotionSlotCount: character.PotionSlotCount,
                Relics: ImmutableArray<string>.Empty,
                RewardRngState: new RewardRngState(
                    rewardTable.PotionDynamic.InitialPercent,
                    rewardTable.EpicChance.InitialBonus),
                ActiveReward: null,
                ActiveMerchant: null,
                ActiveEvent: null,
                ActiveRestPending: false,
                ActiveRestCompleted: false,
                ActiveActStartRelicChoice: null,
                SeenCardBaseIds: ImmutableArray.CreateRange(character.Deck.Distinct()),
                AcquiredRelicIds: ImmutableArray<string>.Empty,
                AcquiredPotionIds: ImmutableArray<string>.Empty));
        }

        return state with
        {
            HostMemberId = RunPartyAccess.DefaultHostMemberId,
            PartyMembers = members.MoveToImmutable(),
        };
    }

    /// <summary>構造的不変条件を検査する。違反があれば理由文字列、問題なければ null。</summary>
    public string? Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
            return $"SchemaVersion must be {CurrentSchemaVersion} (got {SchemaVersion})";
        if (VisitedNodeIds.IsDefault) return "VisitedNodeIds must not be default";
        // invariant 緩和: act-start relic choice がある間は CurrentNodeId が未 visited でも OK
        if (ActiveActStartRelicChoice is null && VisitedNodeIds.Length > 0
            && !VisitedNodeIds.Contains(CurrentNodeId))
            return $"VisitedNodeIds must contain CurrentNodeId ({CurrentNodeId}) unless act-start relic choice is active";
        if (VisitedNodeIds.Length != VisitedNodeIds.Distinct().Count())
            return "VisitedNodeIds must not contain duplicates";
        foreach (var kv in UnknownResolutions)
        {
            if (kv.Value is TileKind.Unknown or TileKind.Start or TileKind.Boss)
                return $"UnknownResolutions[{kv.Key}]={kv.Value} is not a valid resolved kind";
        }
        if (Potions.Length != PotionSlotCount)
            return $"Potions.Length ({Potions.Length}) != PotionSlotCount ({PotionSlotCount})";

        var members = RunPartyAccess.EffectiveMembers(this);
        if (members.Length is < 1 or > 4)
            return $"PartyMembers must contain between 1 and 4 members (got {members.Length})";
        if (members.Any(member => string.IsNullOrWhiteSpace(member.MemberId)))
            return "PartyMembers must not contain empty MemberId values";
        if (members.Select(member => member.MemberId).Distinct(StringComparer.Ordinal).Count() != members.Length)
            return "PartyMembers must not contain duplicate MemberId values";
        if (members.Select(member => member.CharacterId).Distinct(StringComparer.Ordinal).Count() != members.Length)
            return "PartyMembers must not contain duplicate CharacterId values";
        if (members.Select(member => member.SlotIndex).Distinct().Count() != members.Length)
            return "PartyMembers must not contain duplicate SlotIndex values";
        if (!members.Select(member => member.SlotIndex)
                .OrderBy(slot => slot)
                .SequenceEqual(Enumerable.Range(0, members.Length)))
            return "PartyMembers SlotIndex values must be contiguous from 0";
        if (!members.Any(member => string.Equals(
                member.MemberId,
                RunPartyAccess.EffectiveHostMemberId(this),
                StringComparison.Ordinal)))
            return "HostMemberId must reference a party member";
        foreach (var member in members)
        {
            if (string.IsNullOrWhiteSpace(member.CharacterId))
                return $"Party member '{member.MemberId}' CharacterId must not be empty";
            if (member.MaxHp <= 0)
                return $"Party member '{member.MemberId}' MaxHp must be positive";
            if (member.CurrentHp < 0 || member.CurrentHp > member.MaxHp)
                return $"Party member '{member.MemberId}' CurrentHp must be between 0 and MaxHp";
            if (member.Gold < 0)
                return $"Party member '{member.MemberId}' Gold must not be negative";
            if (member.PotionSlotCount < 0)
                return $"Party member '{member.MemberId}' PotionSlotCount must not be negative";
            if (member.Deck.IsDefault)
                return $"Party member '{member.MemberId}' Deck must not be default";
            if (member.Potions.IsDefault)
                return $"Party member '{member.MemberId}' Potions must not be default";
            if (member.Potions.Length != member.PotionSlotCount)
                return $"Party member '{member.MemberId}' potion count does not match PotionSlotCount";
            if (member.Relics.IsDefault)
                return $"Party member '{member.MemberId}' Relics must not be default";
            if (member.SeenCardBaseIds.IsDefault
                || member.AcquiredRelicIds.IsDefault
                || member.AcquiredPotionIds.IsDefault)
                return $"Party member '{member.MemberId}' collection tracking arrays must not be default";

            var memberActiveError = ValidateMemberActiveState(member);
            if (memberActiveError is not null)
                return $"Party member '{member.MemberId}': {memberActiveError}";
        }

        int activeCount = 0;
        if (ActiveBattle is not null) activeCount++;
        if (ActiveReward is not null) activeCount++;
        if (ActiveMerchant is not null) activeCount++;
        if (ActiveEvent is not null) activeCount++;
        if (ActiveActStartRelicChoice is not null) activeCount++;
        // A resolved event may legitimately open a card reward. Treat that pair as
        // one flow while keeping unresolved Event + Reward and every third Active*
        // combination invalid.
        if (ActiveEvent is { ChosenIndex: not null } && ActiveReward is not null)
            activeCount--;
        if (activeCount > 1)
            return "at most one of ActiveBattle / ActiveReward / ActiveMerchant / ActiveEvent / ActiveActStartRelicChoice can be non-null";
        if (ActiveRestPending && activeCount > 0)
            return "ActiveRestPending must not coexist with any other Active*";
        if (ActiveRestCompleted && !ActiveRestPending)
            return "ActiveRestCompleted requires ActiveRestPending";

        if (ActiveReward is { CardChoices: var cc })
        {
            if (cc.IsDefault)
                return "CardChoices must not be default";
            if (cc.Any(string.IsNullOrWhiteSpace))
                return "CardChoices must not contain empty card IDs";
            if (cc.Length != cc.Distinct(StringComparer.Ordinal).Count())
                return "CardChoices must not contain duplicates";
        }
        if (ActiveActStartRelicChoice is { RelicIds: var ids })
        {
            if (ids.IsDefault)
                return "ActStartRelicChoice.RelicIds must not be default";
            if (ids.Length != 3)
                return $"ActStartRelicChoice.RelicIds must have length 3 (got {ids.Length})";
        }
        return null;
    }

    private static string? ValidateMemberActiveState(RunPartyMemberState member)
    {
        var activeCount = 0;
        if (member.ActiveReward is not null) activeCount++;
        if (member.ActiveMerchant is not null) activeCount++;
        if (member.ActiveEvent is not null) activeCount++;
        if (member.ActiveActStartRelicChoice is not null) activeCount++;
        if (member.ActiveEvent is { ChosenIndex: not null } && member.ActiveReward is not null)
            activeCount--;
        if (activeCount > 1)
            return "at most one member-owned Active* flow can be active";
        if (member.ActiveRestPending && activeCount > 0)
            return "ActiveRestPending must not coexist with another member-owned Active* flow";
        if (member.ActiveRestCompleted && !member.ActiveRestPending)
            return "ActiveRestCompleted requires ActiveRestPending";

        if (member.ActiveReward is { CardChoices: var choices })
        {
            if (choices.IsDefault)
                return "CardChoices must not be default";
            if (choices.Any(string.IsNullOrWhiteSpace))
                return "CardChoices must not contain empty card IDs";
            if (choices.Length != choices.Distinct(StringComparer.Ordinal).Count())
                return "CardChoices must not contain duplicates";
        }
        if (member.ActiveActStartRelicChoice is { RelicIds: var relicIds })
        {
            if (relicIds.IsDefault)
                return "ActStartRelicChoice.RelicIds must not be default";
            if (relicIds.Length != 3)
                return $"ActStartRelicChoice.RelicIds must have length 3 (got {relicIds.Length})";
        }

        return null;
    }
}
