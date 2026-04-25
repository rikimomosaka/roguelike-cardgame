using System;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Run;

public static class ActTransition
{
    public static RunState AdvanceAct(
        RunState state, DungeonMap oldMap, DungeonMap newMap, DataCatalog catalog, IRng rng,
        ImmutableDictionary<int, TileKind>? unknownResolutions = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(oldMap);
        ArgumentNullException.ThrowIfNull(newMap);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(rng);

        int nextAct = state.CurrentAct + 1;
        var queueWeak = EncounterQueue.Initialize(new EnemyPool(nextAct, EnemyTier.Weak), catalog, rng);
        var queueStrong = EncounterQueue.Initialize(new EnemyPool(nextAct, EnemyTier.Strong), catalog, rng);
        var queueElite = EncounterQueue.Initialize(new EnemyPool(nextAct, EnemyTier.Elite), catalog, rng);
        var queueBoss = EncounterQueue.Initialize(new EnemyPool(nextAct, EnemyTier.Boss), catalog, rng);

        var oldActEntries = JourneyLogger.EntriesFor(state, oldMap);
        var existingLog = state.JourneyLog.IsDefault
            ? ImmutableArray<JourneyEntry>.Empty
            : state.JourneyLog;

        return state with
        {
            CurrentAct = nextAct,
            CurrentHp = state.MaxHp,
            CurrentNodeId = newMap.StartNodeId,
            VisitedNodeIds = ImmutableArray<int>.Empty,
            UnknownResolutions = unknownResolutions ?? ImmutableDictionary<int, TileKind>.Empty,
            ActiveBattle = null,
            ActiveReward = null,
            ActiveMerchant = null,
            ActiveEvent = null,
            ActiveRestPending = false,
            ActiveRestCompleted = false,
            ActiveActStartRelicChoice = null,
            EncounterQueueWeak = queueWeak,
            EncounterQueueStrong = queueStrong,
            EncounterQueueElite = queueElite,
            EncounterQueueBoss = queueBoss,
            JourneyLog = existingLog.AddRange(oldActEntries),
            SavedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    public static RunState FinishRun(RunState state, RunProgress outcome)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (outcome == RunProgress.InProgress)
            throw new ArgumentException("FinishRun cannot be called with InProgress", nameof(outcome));
        return state with
        {
            Progress = outcome,
            SavedAtUtc = DateTimeOffset.UtcNow,
        };
    }
}
