using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Enemy;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle;

/// <summary>
/// プール単位の encounter 非重複キュー。Run 開始時にシャッフルして格納し、
/// Draw で先頭を取りつつ末尾に push することで「一巡するまで同じ encounter が再出現しない」。
/// </summary>
public static class EncounterQueue
{
    public static ImmutableArray<string> Initialize(EnemyPool pool, DataCatalog data, IRng rng)
    {
        var ids = data.Encounters.Values.Where(e => e.Pool == pool).Select(e => e.Id).ToList();
        // Fisher-Yates
        for (int i = ids.Count - 1; i > 0; i--)
        {
            int j = rng.NextInt(0, i + 1);
            (ids[i], ids[j]) = (ids[j], ids[i]);
        }
        return ids.ToImmutableArray();
    }

    public static (string encounterId, ImmutableArray<string> newQueue) Draw(ImmutableArray<string> queue)
    {
        if (queue.IsEmpty) throw new System.InvalidOperationException("encounter queue is empty");
        var head = queue[0];
        var rest = queue.RemoveAt(0).Add(head);
        return (head, rest);
    }
}
