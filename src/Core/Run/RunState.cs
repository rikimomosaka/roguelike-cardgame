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
    BattleState? ActiveBattle,
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
    int DiscardUsesSoFar = 0)
{
    /// <summary>Phase 7 の JSON スキーマバージョン。</summary>
    public const int CurrentSchemaVersion = 5;

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

        return new RunState(
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
            ActiveActStartRelicChoice: null);
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

        int activeCount = 0;
        if (ActiveBattle is not null) activeCount++;
        if (ActiveReward is not null) activeCount++;
        if (ActiveMerchant is not null) activeCount++;
        if (ActiveEvent is not null) activeCount++;
        if (ActiveActStartRelicChoice is not null) activeCount++;
        if (activeCount > 1)
            return "at most one of ActiveBattle / ActiveReward / ActiveMerchant / ActiveEvent / ActiveActStartRelicChoice can be non-null";
        if (ActiveRestPending && activeCount > 0)
            return "ActiveRestPending must not coexist with any other Active*";
        if (ActiveRestCompleted && !ActiveRestPending)
            return "ActiveRestCompleted requires ActiveRestPending";

        if (ActiveReward is { CardChoices: var cc } && cc.Length != 0 && cc.Length != 3)
            return $"CardChoices must have length 0 or 3 (got {cc.Length})";
        if (ActiveActStartRelicChoice is { RelicIds: var ids } && ids.Length != 3)
            return $"ActStartRelicChoice.RelicIds must have length 3 (got {ids.Length})";
        return null;
    }
}
