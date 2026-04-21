using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
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
    ImmutableArray<string> Deck,
    ImmutableArray<string> Potions,
    int PotionSlotCount,
    BattleState? ActiveBattle,
    RewardState? ActiveReward,
    ImmutableArray<string> EncounterQueueWeak,
    ImmutableArray<string> EncounterQueueStrong,
    ImmutableArray<string> EncounterQueueElite,
    ImmutableArray<string> EncounterQueueBoss,
    RewardRngState RewardRngState,

    // --- existing ---
    IReadOnlyList<string> Relics,
    long PlaySeconds,
    ulong RngSeed,
    DateTimeOffset SavedAtUtc,
    RunProgress Progress)
{
    /// <summary>Phase 5 の JSON スキーマバージョン。</summary>
    public const int CurrentSchemaVersion = 3;

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
        string characterId = "default")
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

        return new RunState(
            SchemaVersion: CurrentSchemaVersion,
            CurrentAct: 1,
            CurrentNodeId: startNodeId,
            VisitedNodeIds: ImmutableArray.Create(startNodeId),
            UnknownResolutions: unknownResolutions,
            CharacterId: characterId,
            CurrentHp: ch.MaxHp,
            MaxHp: ch.MaxHp,
            Gold: ch.StartingGold,
            Deck: ImmutableArray.CreateRange(ch.Deck),
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
            Relics: Array.Empty<string>(),
            PlaySeconds: 0L,
            RngSeed: rngSeed,
            SavedAtUtc: nowUtc,
            Progress: RunProgress.InProgress);
    }

    /// <summary>
    /// 構造的不変条件を検査する。違反があれば理由文字列、問題なければ null。
    /// </summary>
    public string? Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
            return $"SchemaVersion must be {CurrentSchemaVersion} (got {SchemaVersion})";
        if (VisitedNodeIds.IsDefault) return "VisitedNodeIds must not be default";
        if (!VisitedNodeIds.Contains(CurrentNodeId))
            return $"VisitedNodeIds must contain CurrentNodeId ({CurrentNodeId})";
        if (VisitedNodeIds.Length != VisitedNodeIds.Distinct().Count())
            return "VisitedNodeIds must not contain duplicates";
        foreach (var kv in UnknownResolutions)
        {
            if (kv.Value is TileKind.Unknown or TileKind.Start or TileKind.Boss)
                return $"UnknownResolutions[{kv.Key}]={kv.Value} is not a valid resolved kind";
        }
        if (Potions.Length != PotionSlotCount)
            return $"Potions.Length ({Potions.Length}) != PotionSlotCount ({PotionSlotCount})";
        if (ActiveBattle is not null && ActiveReward is not null)
            return "ActiveBattle and ActiveReward must not both be non-null";
        if (ActiveReward is { CardChoices: var cc } && cc.Length != 0 && cc.Length != 3)
            return $"CardChoices must have length 0 or 3 (got {cc.Length})";
        return null;
    }
}
