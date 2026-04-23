using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.History;

public static class RunHistoryBuilder
{
    public static RunHistoryRecord From(
        string accountId, RunState state, int nodesVisited, RunProgress outcome)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        ArgumentNullException.ThrowIfNull(state);
        return new RunHistoryRecord(
            SchemaVersion: RunHistoryRecord.CurrentSchemaVersion,
            AccountId: accountId,
            RunId: state.RunId,
            Outcome: outcome,
            ActReached: state.CurrentAct,
            NodesVisited: nodesVisited,
            PlaySeconds: state.PlaySeconds,
            CharacterId: state.CharacterId,
            FinalHp: state.CurrentHp,
            FinalMaxHp: state.MaxHp,
            FinalGold: state.Gold,
            FinalDeck: state.Deck,
            FinalRelics: state.Relics.ToImmutableArray(),
            EndedAtUtc: DateTimeOffset.UtcNow,
            SeenCardBaseIds: state.SeenCardBaseIds.IsDefault ? ImmutableArray<string>.Empty : state.SeenCardBaseIds,
            AcquiredRelicIds: state.AcquiredRelicIds.IsDefault ? ImmutableArray<string>.Empty : state.AcquiredRelicIds,
            AcquiredPotionIds: state.AcquiredPotionIds.IsDefault ? ImmutableArray<string>.Empty : state.AcquiredPotionIds,
            EncounteredEnemyIds: state.EncounteredEnemyIds.IsDefault ? ImmutableArray<string>.Empty : state.EncounteredEnemyIds);
    }
}
