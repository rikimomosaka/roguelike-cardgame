using System.Collections.Generic;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Run;

namespace RoguelikeCardGame.Core.Bestiary;

/// <summary>RunState の発見セットに ID を和集合で追加する純関数群。</summary>
public static class BestiaryTracker
{
    public static RunState NoteCardsSeen(RunState s, IEnumerable<string>? baseIds)
        => WithUnion(s, baseIds, s.SeenCardBaseIds, a => s with { SeenCardBaseIds = a });

    public static RunState NoteRelicsAcquired(RunState s, IEnumerable<string>? ids)
        => WithUnion(s, ids, s.AcquiredRelicIds, a => s with { AcquiredRelicIds = a });

    public static RunState NotePotionsAcquired(RunState s, IEnumerable<string>? ids)
        => WithUnion(s, ids, s.AcquiredPotionIds, a => s with { AcquiredPotionIds = a });

    public static RunState NoteEnemiesEncountered(RunState s, IEnumerable<string>? ids)
        => WithUnion(s, ids, s.EncounteredEnemyIds, a => s with { EncounteredEnemyIds = a });

    private static RunState WithUnion(
        RunState s,
        IEnumerable<string>? incoming,
        ImmutableArray<string> current,
        System.Func<ImmutableArray<string>, RunState> apply)
    {
        if (incoming is null) return s;
        var existing = current.IsDefault
            ? ImmutableHashSet<string>.Empty
            : current.ToImmutableHashSet();
        var builder = existing.ToBuilder();
        bool changed = false;
        foreach (var id in incoming)
        {
            if (string.IsNullOrEmpty(id)) continue;
            if (builder.Add(id)) changed = true;
        }
        if (!changed) return s;
        var result = builder.ToImmutable();
        return apply(ImmutableArray.CreateRange(result));
    }
}
