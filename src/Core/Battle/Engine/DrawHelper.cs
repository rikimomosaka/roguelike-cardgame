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
        => Draw(
            state, count, rng, HandCap, explicitOwner: null,
            out actuallyDrawn, out _);

    /// <summary>
    /// Draws cards and reports how many discard-pile cards were moved into a
    /// newly shuffled draw pile. A value of zero means no reshuffle occurred.
    /// </summary>
    public static BattleState Draw(
        BattleState state,
        int count,
        IRng rng,
        out int actuallyDrawn,
        out int shuffledCardCount) => Draw(
            state, count, rng, HandCap, explicitOwner: null,
            out actuallyDrawn, out shuffledCardCount);

    /// <summary>
    /// Participant-aware draw variant. The caller supplies the actor whose
    /// hand-only statuses apply and the participant's configured hand limit.
    /// </summary>
    public static BattleState Draw(
        BattleState state,
        int count,
        IRng rng,
        int handCap,
        CombatActor? explicitOwner,
        out int actuallyDrawn)
        => Draw(
            state, count, rng, handCap, explicitOwner,
            out actuallyDrawn, out _);

    /// <summary>
    /// Participant-aware draw variant with an authoritative reshuffle result
    /// for presentation-event emitters.
    /// </summary>
    public static BattleState Draw(
        BattleState state,
        int count,
        IRng rng,
        int handCap,
        CombatActor? explicitOwner,
        out int actuallyDrawn,
        out int shuffledCardCount)
    {
        actuallyDrawn = 0;
        shuffledCardCount = 0;
        if (count <= 0) return state;

        handCap = System.Math.Max(0, handCap);

        var hand = state.Hand.ToBuilder();
        var draw = state.DrawPile.ToBuilder();
        var discard = state.DiscardPile.ToBuilder();

        for (int i = 0; i < count; i++)
        {
            if (hand.Count >= handCap) break;
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
                shuffledCardCount = arr.Length;
            }
            var top = draw[0];
            draw.RemoveAt(0);
            top = BattleStatusRules.ApplyConfusionOnHandEntry(
                state, top, rng, explicitOwner);
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
