using System;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.History;

public sealed record RunHistoryRecord(
    int SchemaVersion,
    string AccountId,
    string RunId,
    RunProgress Outcome,
    int ActReached,
    int NodesVisited,
    long PlaySeconds,
    string CharacterId,
    int FinalHp,
    int FinalMaxHp,
    int FinalGold,
    ImmutableArray<CardInstance> FinalDeck,
    ImmutableArray<string> FinalRelics,
    DateTimeOffset EndedAtUtc,
    ImmutableArray<string> SeenCardBaseIds,
    ImmutableArray<string> AcquiredRelicIds,
    ImmutableArray<string> AcquiredPotionIds,
    ImmutableArray<string> EncounteredEnemyIds,
    // v3: 全アクトに渡る走行履歴。アクト遷移ごとに RunState.JourneyLog へ追記され、
    // ラン終了時に最終アクト分も含めてここへコピーされる。
    ImmutableArray<JourneyEntry> JourneyLog)
{
    public const int CurrentSchemaVersion = 3;
}
