using System;
using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Player;

namespace RoguelikeCardGame.Core.Run;

/// <summary>ソロ／マルチ共通のラン 1 回分の状態。ソロのみ SaveRepository で永続化される。</summary>
public sealed record RunState(
    int SchemaVersion,
    int CurrentAct,
    int CurrentTileIndex,
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
    /// <summary>Phase 1 の JSON スキーマバージョン。</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>初期最大 HP。</summary>
    public const int StartingMaxHp = 80;

    /// <summary>初期所持金。</summary>
    public const int StartingGold = 99;

    /// <summary>新規ソロラン状態を作る。StarterDeck が DataCatalog に存在することを検証。</summary>
    public static RunState NewSoloRun(DataCatalog catalog, ulong rngSeed, DateTimeOffset nowUtc)
    {
        foreach (var id in StarterDeck.DefaultCardIds)
        {
            if (!catalog.TryGetCard(id, out _))
                throw new InvalidOperationException(
                    $"StarterDeck が参照するカード ID が DataCatalog に存在しません: {id}");
        }

        // 防御的コピー: string[] は IReadOnlyList<string> を実装。
        var deck = StarterDeck.DefaultCardIds.ToArray();

        return new RunState(
            SchemaVersion: CurrentSchemaVersion,
            CurrentAct: 1,
            CurrentTileIndex: 0,
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
}
