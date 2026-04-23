using System.Collections.Generic;

namespace RoguelikeCardGame.Server.Dtos;

public sealed record RunResultCardDto(string Id, bool Upgraded);

public sealed record RunResultDto(
    int SchemaVersion,
    string AccountId,
    string RunId,
    string Outcome,
    int ActReached,
    int NodesVisited,
    long PlaySeconds,
    string CharacterId,
    int FinalHp,
    int FinalMaxHp,
    int FinalGold,
    IReadOnlyList<RunResultCardDto> FinalDeck,
    IReadOnlyList<string> FinalRelics,
    string EndedAtUtc,
    IReadOnlyList<string> SeenCardBaseIds,
    IReadOnlyList<string> AcquiredRelicIds,
    IReadOnlyList<string> AcquiredPotionIds,
    IReadOnlyList<string> EncounteredEnemyIds);
