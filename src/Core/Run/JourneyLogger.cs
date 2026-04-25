using System.Collections.Immutable;
using RoguelikeCardGame.Core.Map;

namespace RoguelikeCardGame.Core.Run;

/// <summary>RunState.VisitedNodeIds と DungeonMap から JourneyEntry 列を生成する純関数。</summary>
public static class JourneyLogger
{
    public static ImmutableArray<JourneyEntry> EntriesFor(RunState state, DungeonMap map)
    {
        if (state.VisitedNodeIds.IsDefaultOrEmpty)
            return ImmutableArray<JourneyEntry>.Empty;
        var b = ImmutableArray.CreateBuilder<JourneyEntry>(state.VisitedNodeIds.Length);
        foreach (var nid in state.VisitedNodeIds)
        {
            var node = map.GetNode(nid);
            TileKind? resolved = state.UnknownResolutions.TryGetValue(nid, out var rk) ? rk : null;
            b.Add(new JourneyEntry(state.CurrentAct, nid, node.Kind, resolved));
        }
        return b.ToImmutable();
    }
}
