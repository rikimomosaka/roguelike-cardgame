using System.Collections.Generic;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// ターン開始処理。10.2.A は最小限（ターン+1, Energy 全回復, 5 ドロー）。
/// 10.2.B で 毒・状態異常 tick / 召喚 Lifetime tick / OnTurnStart レリックが追加される。
/// 親 spec §4-2 参照。
/// </summary>
internal static class TurnStartProcessor
{
    public const int DrawPerTurn = 5;
    public const int HandCap = 10;

    public static (BattleState, IReadOnlyList<BattleEvent>) Process(BattleState state, IRng rng)
    {
        var s = state with
        {
            Turn = state.Turn + 1,
            Energy = state.EnergyMax,
        };
        s = DrawCards(s, DrawPerTurn, rng);
        var events = new List<BattleEvent>
        {
            new(BattleEventKind.TurnStart, Order: 0, Note: $"turn={s.Turn}"),
        };
        return (s, events);
    }

    private static BattleState DrawCards(BattleState state, int count, IRng rng)
    {
        var hand = state.Hand.ToBuilder();
        var draw = state.DrawPile.ToBuilder();
        var discard = state.DiscardPile.ToBuilder();

        for (int i = 0; i < count; i++)
        {
            if (hand.Count >= HandCap) break;
            if (draw.Count == 0)
            {
                if (discard.Count == 0) break;
                ShuffleInto(discard, draw, rng);
                discard.Clear();
            }
            // 山札先頭から取り出す
            var top = draw[0];
            draw.RemoveAt(0);
            hand.Add(top);
        }

        return state with
        {
            Hand = hand.ToImmutable(),
            DrawPile = draw.ToImmutable(),
            DiscardPile = discard.ToImmutable(),
        };
    }

    /// <summary>
    /// Fisher-Yates シャッフル。`source` の中身を `dest` に移しながらランダム順で並べる。
    /// </summary>
    private static void ShuffleInto(
        ImmutableArray<BattleCardInstance>.Builder source,
        ImmutableArray<BattleCardInstance>.Builder dest,
        IRng rng)
    {
        var arr = source.ToArray();
        for (int i = arr.Length - 1; i > 0; i--)
        {
            int j = rng.NextInt(0, i + 1);
            (arr[i], arr[j]) = (arr[j], arr[i]);
        }
        foreach (var c in arr) dest.Add(c);
    }
}
