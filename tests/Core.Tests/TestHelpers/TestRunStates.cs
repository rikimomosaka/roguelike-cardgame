using System;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Map;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Tests;

public static class TestRunStates
{
    public static RunState FreshDefault(DataCatalog cat)
    {
        return RunState.NewSoloRun(
            cat,
            rngSeed: 0,
            startNodeId: 0,
            unknownResolutions: ImmutableDictionary<int, TileKind>.Empty,
            encounterQueueWeak: ImmutableArray<string>.Empty,
            encounterQueueStrong: ImmutableArray<string>.Empty,
            encounterQueueElite: ImmutableArray<string>.Empty,
            encounterQueueBoss: ImmutableArray<string>.Empty,
            nowUtc: DateTimeOffset.UnixEpoch);
    }
}
