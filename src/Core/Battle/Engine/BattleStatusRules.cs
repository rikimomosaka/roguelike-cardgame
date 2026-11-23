using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>Small, deterministic operations shared by the status integrations.</summary>
internal static class BattleStatusRules
{
    public static ImmutableArray<string> NormalizeStatusDisplayOrder(CombatActor actor)
    {
        var current = actor.StatusDisplayOrder
            .Where(id => actor.Statuses.ContainsKey(id))
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();
        var known = current.ToHashSet(StringComparer.Ordinal);
        var missing = StatusDefinition.All.Select(definition => definition.Id)
            .Concat(actor.Statuses.Keys.OrderBy(id => id, StringComparer.Ordinal))
            .Where(id => actor.Statuses.ContainsKey(id) && known.Add(id));
        return current.AddRange(missing);
    }

    public static CombatActor Decrement(CombatActor actor, string id, int amount = 1)
    {
        int remaining = actor.GetStatus(id) - Math.Max(0, amount);
        return remaining <= 0
            ? actor with
            {
                Statuses = actor.Statuses.Remove(id),
                StatusSources = actor.StatusSources.Remove(id),
                StatusDisplayOrder = NormalizeStatusDisplayOrder(actor).Remove(id),
            }
            : actor with { Statuses = actor.Statuses.SetItem(id, remaining) };
    }

    public static CombatActor Clear(CombatActor actor, string id) => actor with
    {
        Statuses = actor.Statuses.Remove(id),
        StatusSources = actor.StatusSources.Remove(id),
        StatusDisplayOrder = NormalizeStatusDisplayOrder(actor).Remove(id),
    };

    public static CombatActor AddSaturating(CombatActor actor, string id, int amount)
    {
        long value = (long)actor.GetStatus(id) + amount;
        int next = (int)Math.Clamp(value, int.MinValue, int.MaxValue);
        if (next == 0) return Clear(actor, id);
        bool isNew = actor.GetStatus(id) == 0;
        return actor with
        {
            Statuses = actor.Statuses.SetItem(id, next),
            StatusDisplayOrder = isNew
                ? NormalizeStatusDisplayOrder(actor).Remove(id).Add(id)
                : NormalizeStatusDisplayOrder(actor),
        };
    }

    public static BattleCardInstance ApplyConfusionOnHandEntry(
        BattleState state,
        BattleCardInstance card,
        IRng rng,
        CombatActor? explicitOwner = null)
    {
        var owner = explicitOwner
            ?? ProvisionalSoloHeroOwnerResolver.Resolve(state)?.Hero;
        if (owner?.GetStatus(BattleStatusIds.Confusion) <= 0)
            return card;

        // NextInt upper bound is exclusive: all four values have equal 25% weight.
        return card with
        {
            CostOverride = rng.NextInt(0, 4),
            // Confusion is a fixed replacement. Any older per-card additive
            // changes are discarded at the instant the card enters the hand.
            CostModifiers = ImmutableArray<CardCostModifier>.Empty,
        };
    }

    /// <summary>
    /// Consumes Revive after lethal damage and restores HP. Revive's X is the
    /// restored amount as requested, not a stack count.
    /// </summary>
    public static bool TryRevive(CombatActor actor, out CombatActor revived)
    {
        int amount = actor.GetStatus(BattleStatusIds.Revive);
        if (actor.CurrentHp > 0 || amount <= 0 || actor.MaxHp <= 0)
        {
            revived = actor;
            return false;
        }

        revived = Clear(actor, BattleStatusIds.Revive) with
        {
            CurrentHp = Math.Min(actor.MaxHp, amount),
        };
        return true;
    }
}
