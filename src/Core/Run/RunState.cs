using System;

namespace RoguelikeCardGame.Core.Run;

/// <summary>ソロ／マルチ共通のラン 1 回分の状態。ソロのみ SaveRepository で永続化される。</summary>
public sealed record RunState(
    int SchemaVersion,
    int CurrentAct,
    int CurrentTileIndex,
    int CurrentHp,
    int MaxHp,
    int Gold,
    string[] Deck,
    string[] Relics,
    string[] Potions,
    long PlaySeconds,
    ulong RngSeed,
    DateTimeOffset SavedAtUtc,
    RunProgress Progress);
