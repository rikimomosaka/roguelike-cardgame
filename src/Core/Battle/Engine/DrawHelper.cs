using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// Hand 増分の共通ヘルパー。Phase 10.2.E (W5 修正) で TurnStartProcessor.DrawCards と
/// EffectApplier.ApplyDraw の Fisher-Yates シャッフル + Hand 追加ロジック重複を解消。
/// HandCap (10) もここで一元化。
/// </summary>
internal static class DrawHelper
{
    public const int HandCap = 10;

    /// <summary>
    /// state.Hand に最大 count 枚追加。山札不足時は捨札を Fisher-Yates シャッフルして補充。
    /// HandCap で打ち切り。実際にドローした枚数を out で返す。
    /// </summary>
    public static BattleState Draw(BattleState state, int count, IRng rng, out int actuallyDrawn)
    {
        actuallyDrawn = 0;
        if (count <= 0) return state;

        var hand = state.Hand.ToBuilder();
        var draw = state.DrawPile.ToBuilder();
        var discard = state.DiscardPile.ToBuilder();

        for (int i = 0; i < count; i++)
        {
            if (hand.Count >= HandCap) break;
            if (draw.Count == 0)
            {
                if (discard.Count == 0) break;
                // Fisher-Yates shuffle: discard → draw
                var arr = discard.ToArray();
                for (int j = arr.Length - 1; j > 0; j--)
                {
                    int k = rng.NextInt(0, j + 1);
                    (arr[j], arr[k]) = (arr[k], arr[j]);
                }
                foreach (var c in arr) draw.Add(c);
                discard.Clear();
            }
            var top = draw[0];
            draw.RemoveAt(0);
            hand.Add(top);
            actuallyDrawn++;
        }

        if (actuallyDrawn == 0) return state;

        return state with
        {
            Hand = hand.ToImmutable(),
            DrawPile = draw.ToImmutable(),
            DiscardPile = discard.ToImmutable(),
        };
    }
}
