using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Player;

namespace RoguelikeCardGame.Core.Run;

/// <summary>ソロ／マルチ共通のラン 1 回分の状態。ソロのみ ISaveRepository で永続化される。</summary>
public sealed record RunState(
    int SchemaVersion,
    int CurrentAct,
    int CurrentNodeId,
    ImmutableArray<int> VisitedNodeIds,
    ImmutableDictionary<int, TileKind> UnknownResolutions,
    int CurrentHp,
    int MaxHp,
    int Gold,
    IReadOnlyList<string> Deck,
    IReadOnlyList<string> Relics,
    IReadOnlyList<string> Potions,
    long PlaySeconds,
    ulong RngSeed,
    DateTimeOffset SavedAtUtc,
    RunProgress Progress)
{
    /// <summary>Phase 4 の JSON スキーマバージョン。</summary>
    public const int CurrentSchemaVersion = 2;

    public const int StartingMaxHp = 80;
    public const int StartingGold = 99;

    public static RunState NewSoloRun(
        DataCatalog catalog,
        ulong rngSeed,
        int startNodeId,
        ImmutableDictionary<int, TileKind> unknownResolutions,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(unknownResolutions);

        foreach (var id in StarterDeck.DefaultCardIds)
        {
            if (!catalog.TryGetCard(id, out _))
                throw new InvalidOperationException(
                    $"StarterDeck が参照するカード ID が DataCatalog に存在しません: {id}");
        }

        var deck = StarterDeck.DefaultCardIds.ToArray();

        return new RunState(
            SchemaVersion: CurrentSchemaVersion,
            CurrentAct: 1,
            CurrentNodeId: startNodeId,
            VisitedNodeIds: ImmutableArray.Create(startNodeId),
            UnknownResolutions: unknownResolutions,
            CurrentHp: StartingMaxHp,
            MaxHp: StartingMaxHp,
            Gold: StartingGold,
            Deck: deck,
            Relics: Array.Empty<string>(),
            Potions: Array.Empty<string>(),
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
        return null;
    }
}
