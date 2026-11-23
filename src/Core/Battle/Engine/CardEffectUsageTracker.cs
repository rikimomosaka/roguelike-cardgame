using System;
using RoguelikeCardGame.Core.Battle.State;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// Tracks a card definition's per-turn usage limit. Normal/upgraded copies and
/// every instance of the same base definition intentionally share the count.
/// </summary>
public static class CardUsageTracker
{
    public static int Count(BattleState state, string cardDefinitionId) =>
        state.CardUsesThisTurn.TryGetValue(cardDefinitionId, out var count)
            ? Math.Max(0, count)
            : 0;

    public static bool HasReachedLimit(
        BattleState state,
        string cardDefinitionId,
        int limit) =>
        limit > 0 && Count(state, cardDefinitionId) >= limit;

    public static BattleState MarkUsed(
        BattleState state,
        string cardDefinitionId,
        int limit)
    {
        if (limit <= 0) return state;
        var current = Count(state, cardDefinitionId);
        return state with
        {
            CardUsesThisTurn = state.CardUsesThisTurn.SetItem(
                cardDefinitionId,
                current == int.MaxValue ? int.MaxValue : current + 1),
        };
    }
}

/// <summary>Tracks potion-definition uses because potion slots do not yet have instance IDs.</summary>
public static class PotionUsageTracker
{
    public static int Count(BattleState state, string potionId) =>
        state.PotionUsesThisTurn.TryGetValue(potionId, out var count)
            ? Math.Max(0, count)
            : 0;

    public static bool HasReachedLimit(BattleState state, string potionId, int limit) =>
        limit > 0 && Count(state, potionId) >= limit;

    public static BattleState MarkUsed(BattleState state, string potionId, int limit)
    {
        if (limit <= 0) return state;
        var current = Count(state, potionId);
        return state with
        {
            PotionUsesThisTurn = state.PotionUsesThisTurn.SetItem(
                potionId,
                current == int.MaxValue ? int.MaxValue : current + 1),
        };
    }
}
