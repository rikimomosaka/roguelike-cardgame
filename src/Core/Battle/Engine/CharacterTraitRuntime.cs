using System;
using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// Shared dispatcher for data-driven character traits. Callers own the exact
/// lifecycle hook and pass the value transition that caused it; this class
/// owns filtering, root-projected usage, attribution, and effect execution.
/// The supplied state must already project the triggering actor's participant;
/// projection and commit remain the lifecycle caller's responsibility.
/// </summary>
public static class CharacterTraitRuntime
{
    /// <summary>
    /// Tries to activate the character trait owned by <paramref name="actorInstanceId"/>.
    /// For <see cref="CharacterTraitTrigger.OnComboReached"/>, the trait only
    /// activates when its threshold is crossed from below.
    /// </summary>
    public static (BattleState State, IReadOnlyList<BattleEvent> Events) TryTrigger(
        BattleState state,
        string actorInstanceId,
        CharacterTraitTrigger trigger,
        DataCatalog catalog,
        IRng rng,
        int previousValue = 0,
        int currentValue = 0,
        int orderStart = 0)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorInstanceId);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(rng);
        if (orderStart < 0)
            throw new ArgumentOutOfRangeException(nameof(orderStart));

        if (state.Outcome !=
            RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending)
            return (state, Array.Empty<BattleEvent>());
        if (!catalog.TryGetCharacter(state.CharacterId, out var character) ||
            character.Trait is not { } trait ||
            !trait.HasTrigger(trigger))
            return (state, Array.Empty<BattleEvent>());

        // 1 つの特性が複数の発動条件を持てる（clauses）。この契機に合う条件を順に発動する。
        var next = state;
        var allEvents = new List<BattleEvent>();
        int nextOrder = orderStart;
        foreach (var (key, clause) in trait.AllClauses())
        {
            if (clause.Trigger != trigger) continue;
            var (afterClause, clauseEvents) = TryTriggerClause(
                next, actorInstanceId, trait, key, clause, trigger, catalog, rng,
                previousValue, currentValue, nextOrder);
            next = afterClause;
            allEvents.AddRange(clauseEvents);
            nextOrder += clauseEvents.Count;
        }
        return (next, allEvents);
    }

    private static (BattleState State, IReadOnlyList<BattleEvent> Events) TryTriggerClause(
        BattleState state,
        string actorInstanceId,
        CharacterTraitDefinition trait,
        string usageKey,
        CharacterTraitClause clause,
        CharacterTraitTrigger trigger,
        DataCatalog catalog,
        IRng rng,
        int previousValue,
        int currentValue,
        int orderStart)
    {
        if (trigger == CharacterTraitTrigger.OnComboReached &&
            !CrossedThreshold(previousValue, currentValue, clause.Threshold))
            return (state, Array.Empty<BattleEvent>());

        int useCount = state.CharacterTraitUsesThisTurn.TryGetValue(
            usageKey, out var existingCount)
            ? existingCount
            : 0;
        if (clause.UsesPerTurn > 0 && useCount >= clause.UsesPerTurn)
            return (state, Array.Empty<BattleEvent>());

        var caster = state.Allies.FirstOrDefault(actor =>
            actor.IsAlive &&
            string.Equals(actor.InstanceId, actorInstanceId, StringComparison.Ordinal));
        if (caster is null)
            return (state, Array.Empty<BattleEvent>());

        var next = state with
        {
            CharacterTraitUsesThisTurn =
                state.CharacterTraitUsesThisTurn.SetItem(usageKey, useCount + 1),
        };

        var source = new CombatSource(
            CombatSourceKind.CharacterTrait,
            OwnerSlot: caster.OwnerPlayerSlot ?? caster.SlotIndex,
            OwnerInstanceId: caster.OwnerActorInstanceId ?? caster.InstanceId,
            ActorInstanceId: caster.InstanceId,
            DefinitionId: trait.Id,
            Scope: trigger.ToString());
        var events = new List<BattleEvent>
        {
            new(
                BattleEventKind.CharacterTraitTriggered,
                Order: orderStart,
                CasterInstanceId: caster.InstanceId,
                Amount: useCount + 1,
                Note: trait.Id,
                Source: source),
        };

        int order = orderStart + 1;
        foreach (var effect in clause.Effects)
        {
            // Triggered CardEffects belong to the card/relic/power trigger
            // systems. A trait activation executes only its immediate effects.
            if (!string.IsNullOrEmpty(effect.Trigger))
                continue;

            var currentCaster = next.Allies.FirstOrDefault(actor =>
                actor.IsAlive && string.Equals(
                    actor.InstanceId, caster.InstanceId, StringComparison.Ordinal));
            if (currentCaster is null)
                break;

            var (afterEffect, effectEvents) = EffectApplier.Apply(
                next, currentCaster, effect, rng, catalog, source: source);
            next = afterEffect;
            foreach (var effectEvent in effectEvents)
            {
                events.Add(effectEvent with
                {
                    Order = order++,
                    Source = effectEvent.Source ?? source,
                });
            }
        }

        return (next, events);
    }

    /// <summary>
    /// Clears usage on the currently projected participant root. The lifecycle
    /// caller commits this root before projecting the next participant.
    /// </summary>
    public static BattleState ResetTurnUsage(BattleState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state with
        {
            CharacterTraitUsesThisTurn =
                System.Collections.Immutable.ImmutableDictionary<string, int>.Empty,
        };
    }

    private static bool CrossedThreshold(
        int previousValue,
        int currentValue,
        int threshold) =>
        threshold > 0 &&
        previousValue < threshold &&
        currentValue >= threshold;
}
