using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.State;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>期限を迎えたカード個体modifierを全領域から同じ規則で除去する。</summary>
internal static class BattleCardCostModifierLifecycle
{
    public static BattleState Remove(
        BattleState state, CardCostModifierDuration duration) => state with
    {
        DrawPile = Remove(state.DrawPile, duration),
        Hand = Remove(state.Hand, duration),
        DiscardPile = Remove(state.DiscardPile, duration),
        ExhaustPile = Remove(state.ExhaustPile, duration),
        SummonHeld = Remove(state.SummonHeld, duration),
        PowerCards = Remove(state.PowerCards, duration),
    };

    private static ImmutableArray<BattleCardInstance> Remove(
        ImmutableArray<BattleCardInstance> cards,
        CardCostModifierDuration duration)
    {
        if (cards.IsDefaultOrEmpty) return cards;
        return cards.Select(card => card.RemoveCostModifiers(duration)).ToImmutableArray();
    }
}
