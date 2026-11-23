using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>Resolves trigger effects from participant-owned power cards.</summary>
internal static class PowerTriggerProcessor
{
    /// <summary>Legacy/root-compatible entry point. The current root owner fires.</summary>
    public static (BattleState, IReadOnlyList<BattleEvent>) Fire(
        BattleState state, string trigger,
        DataCatalog catalog, IRng rng, int orderStart,
        string? playingCardInstanceId = null)
    {
        var caster = ResolveLegacyRootCaster(state);
        return caster is null
            ? (state, Array.Empty<BattleEvent>())
            : FireProjectedInternal(
                state, caster.InstanceId, trigger, comboCount: null,
                deadEnemyInstanceId: null, catalog, rng, orderStart,
                playingCardInstanceId);
    }

    public static (BattleState, IReadOnlyList<BattleEvent>) FireForActor(
        BattleState state, string actorInstanceId, string trigger,
        DataCatalog catalog, IRng rng, int orderStart,
        string? playingCardInstanceId = null)
        => FireForActorInternal(
            state, actorInstanceId, trigger, comboCount: null,
            deadEnemyInstanceId: null, catalog, rng, orderStart,
            playingCardInstanceId);

    /// <summary>
    /// Entry point for a command whose participant resources are already
    /// projected to the root. Its outer command boundary owns the commit.
    /// </summary>
    public static (BattleState, IReadOnlyList<BattleEvent>) FireFromProjectedOwner(
        BattleState state, string actorInstanceId, string trigger,
        DataCatalog catalog, IRng rng, int orderStart,
        string? playingCardInstanceId = null)
    {
        if (!CanUseProjectedOwner(state, actorInstanceId))
            return (state, Array.Empty<BattleEvent>());

        return FireProjectedInternal(
            state, actorInstanceId, trigger, comboCount: null,
            deadEnemyInstanceId: null, catalog, rng, orderStart,
            playingCardInstanceId);
    }

    public static (BattleState, IReadOnlyList<BattleEvent>) FireOnCombo(
        BattleState state, int comboCount,
        DataCatalog catalog, IRng rng, int orderStart,
        string? playingCardInstanceId = null)
    {
        var caster = ResolveLegacyRootCaster(state);
        return caster is null
            ? (state, Array.Empty<BattleEvent>())
            : FireProjectedInternal(
                state, caster.InstanceId, "OnCombo", comboCount,
                deadEnemyInstanceId: null, catalog, rng, orderStart,
                playingCardInstanceId);
    }

    public static (BattleState, IReadOnlyList<BattleEvent>) FireOnComboForActor(
        BattleState state, string actorInstanceId, int comboCount,
        DataCatalog catalog, IRng rng, int orderStart,
        string? playingCardInstanceId = null)
        => FireForActorInternal(
            state, actorInstanceId, "OnCombo", comboCount,
            deadEnemyInstanceId: null, catalog, rng, orderStart,
            playingCardInstanceId);

    public static (BattleState, IReadOnlyList<BattleEvent>) FireOnComboFromProjectedOwner(
        BattleState state, string actorInstanceId, int comboCount,
        DataCatalog catalog, IRng rng, int orderStart,
        string? playingCardInstanceId = null)
    {
        if (!CanUseProjectedOwner(state, actorInstanceId))
            return (state, Array.Empty<BattleEvent>());

        return FireProjectedInternal(
            state, actorInstanceId, "OnCombo", comboCount,
            deadEnemyInstanceId: null, catalog, rng, orderStart,
            playingCardInstanceId);
    }

    public static (BattleState, IReadOnlyList<BattleEvent>) FireOnDamageReceived(
        BattleState state, DataCatalog catalog, IRng rng, int orderStart,
        string? playingCardInstanceId = null)
    {
        var caster = ResolveLegacyRootCaster(state);
        return caster is null
            ? (state, Array.Empty<BattleEvent>())
            : FireProjectedInternal(
                state, caster.InstanceId, "OnDamageReceived", comboCount: null,
                deadEnemyInstanceId: null, catalog, rng, orderStart,
                playingCardInstanceId);
    }

    public static (BattleState, IReadOnlyList<BattleEvent>) FireOnDamageReceived(
        BattleState state, string actorInstanceId,
        DataCatalog catalog, IRng rng, int orderStart,
        string? playingCardInstanceId = null)
        => FireForActorInternal(
            state, actorInstanceId, "OnDamageReceived", comboCount: null,
            deadEnemyInstanceId: null, catalog, rng, orderStart,
            playingCardInstanceId);

    public static (BattleState, IReadOnlyList<BattleEvent>) FireOnDamageReceivedFromProjectedOwner(
        BattleState state, string actorInstanceId,
        DataCatalog catalog, IRng rng, int orderStart,
        string? playingCardInstanceId = null)
    {
        if (!CanUseProjectedOwner(state, actorInstanceId))
            return (state, Array.Empty<BattleEvent>());

        return FireProjectedInternal(
            state, actorInstanceId, "OnDamageReceived", comboCount: null,
            deadEnemyInstanceId: null, catalog, rng, orderStart,
            playingCardInstanceId);
    }

    public static (BattleState, IReadOnlyList<BattleEvent>) FireForAllLivingParticipants(
        BattleState state, string trigger,
        DataCatalog catalog, IRng rng, int orderStart,
        string? playingCardInstanceId = null)
    {
        if (state.Participants.IsDefaultOrEmpty)
            return Fire(
                state, trigger, catalog, rng, orderStart,
                playingCardInstanceId);

        var participantIds = BattleParticipantAccess.InActorSlotOrder(state)
            .Where(participant => IsLivingAlly(state, participant.ActorInstanceId))
            .Select(participant => participant.ParticipantId)
            .ToArray();
        var current = BattleParticipantAccess.CommitRootToParticipant(
            state, state.Participants[0].ParticipantId);
        var events = new List<BattleEvent>();
        int order = orderStart;

        foreach (string participantId in participantIds)
        {
            if (!BattleParticipantAccess.TryGet(
                    current, participantId, out var participant)
                || !IsLivingAlly(current, participant.ActorInstanceId))
                continue;

            var (after, fired) = FireForActor(
                current, participant.ActorInstanceId, trigger,
                catalog, rng, order, playingCardInstanceId);
            current = after;
            foreach (var battleEvent in fired)
                events.Add(battleEvent with { Order = order++ });
        }

        return (current, events);
    }

    /// <summary>
    /// Enemy death is global: every living participant-owned power fires once.
    /// </summary>
    public static (BattleState, IReadOnlyList<BattleEvent>) FireOnEnemyDeath(
        BattleState state, string deadEnemyInstanceId,
        DataCatalog catalog, IRng rng, int orderStart,
        string? playingCardInstanceId = null)
    {
        if (state.Participants.IsDefaultOrEmpty)
        {
            var caster = ResolveLegacyRootCaster(state);
            return caster is null
                ? (state, Array.Empty<BattleEvent>())
                : FireProjectedInternal(
                    state, caster.InstanceId, "OnEnemyDeath", comboCount: null,
                    deadEnemyInstanceId, catalog, rng, orderStart,
                    playingCardInstanceId);
        }

        var participantIds = BattleParticipantAccess.InActorSlotOrder(state)
            .Where(participant => IsLivingAlly(state, participant.ActorInstanceId))
            .Select(participant => participant.ParticipantId)
            .ToArray();
        var current = BattleParticipantAccess.CommitRootToParticipant(
            state, state.Participants[0].ParticipantId);
        var events = new List<BattleEvent>();
        int order = orderStart;

        foreach (string participantId in participantIds)
        {
            if (!BattleParticipantAccess.TryGet(
                    current, participantId, out var participant)
                || !IsLivingAlly(current, participant.ActorInstanceId))
                continue;

            var projected = BattleParticipantAccess.ProjectToRoot(
                current, participant.ParticipantId);
            var (processed, fired) = FireProjectedInternal(
                projected, participant.ActorInstanceId, "OnEnemyDeath",
                comboCount: null, deadEnemyInstanceId, catalog, rng,
                order, playingCardInstanceId);
            current = BattleParticipantAccess.CommitRootToParticipant(
                processed, participant.ParticipantId);
            foreach (var battleEvent in fired)
                events.Add(battleEvent with { Order = order++ });
        }

        return (current, events);
    }

    /// <summary>
    /// Fires once for one attack target. Ownership comes from the source actor.
    /// </summary>
    public static (BattleState, IReadOnlyList<BattleEvent>) FireOnHit(
        BattleState state, string targetInstanceId, HitContext hitContext,
        DataCatalog catalog, IRng rng, int orderStart,
        string? playingCardInstanceId = null)
    {
        string? actorInstanceId = ResolveSourceActorId(state, hitContext.Source);
        if (actorInstanceId is null)
            return (state, Array.Empty<BattleEvent>());

        if (state.Participants.IsDefaultOrEmpty)
            return FireOnHitProjectedInternal(
                state, actorInstanceId, targetInstanceId, hitContext,
                catalog, rng, orderStart, playingCardInstanceId);

        if (!BattleParticipantAccess.TryGetByActor(
                state, actorInstanceId, out var participant)
            || !IsLivingAlly(state, actorInstanceId))
            return (state, Array.Empty<BattleEvent>());

        var projected = BattleParticipantAccess.ProjectToRoot(
            state, participant.ParticipantId);
        var (processed, events) = FireOnHitProjectedInternal(
            projected, actorInstanceId, targetInstanceId, hitContext,
            catalog, rng, orderStart, playingCardInstanceId);
        return (
            BattleParticipantAccess.CommitRootToParticipant(
                processed, participant.ParticipantId),
            events);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) FireForActorInternal(
        BattleState state, string actorInstanceId, string trigger,
        int? comboCount, string? deadEnemyInstanceId,
        DataCatalog catalog, IRng rng, int orderStart,
        string? playingCardInstanceId)
    {
        if (state.Participants.IsDefaultOrEmpty)
        {
            var legacyCaster = ResolveLegacyRootCaster(state);
            if (legacyCaster is null
                || !string.Equals(
                    legacyCaster.InstanceId, actorInstanceId,
                    StringComparison.Ordinal))
                return (state, Array.Empty<BattleEvent>());

            return FireProjectedInternal(
                state, actorInstanceId, trigger, comboCount,
                deadEnemyInstanceId, catalog, rng, orderStart,
                playingCardInstanceId);
        }

        if (!BattleParticipantAccess.TryGetByActor(
                state, actorInstanceId, out var participant)
            || !IsLivingAlly(state, actorInstanceId))
            return (state, Array.Empty<BattleEvent>());

        var projected = BattleParticipantAccess.ProjectToRoot(
            state, participant.ParticipantId);
        var (processed, events) = FireProjectedInternal(
            projected, actorInstanceId, trigger, comboCount,
            deadEnemyInstanceId, catalog, rng, orderStart,
            playingCardInstanceId);
        return (
            BattleParticipantAccess.CommitRootToParticipant(
                processed, participant.ParticipantId),
            events);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) FireOnHitProjectedInternal(
        BattleState state, string actorInstanceId, string targetInstanceId,
        HitContext hitContext, DataCatalog catalog, IRng rng, int orderStart,
        string? playingCardInstanceId)
    {
        var events = new List<BattleEvent>();
        var current = state;
        int order = orderStart;
        var caster = FindLivingAlly(current, actorInstanceId);
        if (caster is null) return (current, events);

        foreach (var card in current.PowerCards.ToArray())
        {
            if (!catalog.Cards.TryGetValue(
                    card.CardDefinitionId, out var definition))
                continue;
            var effects = card.IsUpgraded && definition.UpgradedEffects is not null
                ? definition.UpgradedEffects
                : definition.Effects;

            for (int effectIndex = 0; effectIndex < effects.Count; effectIndex++)
            {
                var effect = effects[effectIndex];
                if (effect.Trigger != "OnHit" || !PassesChance(effect, rng))
                    continue;

                int? oldEnemyTarget = current.TargetEnemyIndex;
                int? oldAllyTarget = current.TargetAllyIndex;
                int enemyIndex = FindIndex(current.Enemies, targetInstanceId);
                int allyIndex = FindIndex(current.Allies, targetInstanceId);
                if (enemyIndex >= 0)
                    current = current with { TargetEnemyIndex = enemyIndex };
                if (allyIndex >= 0)
                    current = current with { TargetAllyIndex = allyIndex };

                var source = CreateSource(
                    current, caster, card, effectIndex, effect);
                var triggeredContext = hitContext.CreateChild(
                    hitContext.DamageType, source, "OnHit");
                var targetedEffect = TargetOnHitEffect(effect, enemyIndex);
                var (afterEffect, fired) = EffectApplier.Apply(
                    current, caster, targetedEffect, rng, catalog, source,
                    triggeredContext.CauseChain, playingCardInstanceId);
                current = afterEffect with
                {
                    TargetEnemyIndex = oldEnemyTarget,
                    TargetAllyIndex = oldAllyTarget,
                };

                foreach (var battleEvent in fired)
                {
                    string note = string.IsNullOrEmpty(battleEvent.Note)
                        ? $"power:{card.CardDefinitionId};OnHit"
                        : $"{battleEvent.Note};power:{card.CardDefinitionId};OnHit";
                    events.Add(battleEvent with
                    {
                        Order = order++,
                        Note = note,
                        Source = source,
                        CauseChain = triggeredContext.CauseChain,
                    });
                }

                caster = FindLivingAlly(current, actorInstanceId);
                if (caster is null) return (current, events);
            }
        }

        return (current, events);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) FireProjectedInternal(
        BattleState state, string actorInstanceId, string trigger,
        int? comboCount, string? deadEnemyInstanceId,
        DataCatalog catalog, IRng rng, int orderStart,
        string? playingCardInstanceId)
    {
        var events = new List<BattleEvent>();
        var current = state;
        int order = orderStart;
        var caster = FindLivingAlly(current, actorInstanceId);
        if (caster is null) return (current, events);

        foreach (var card in current.PowerCards.ToArray())
        {
            if (!catalog.Cards.TryGetValue(
                    card.CardDefinitionId, out var definition))
                continue;
            var effects = card.IsUpgraded && definition.UpgradedEffects is not null
                ? definition.UpgradedEffects
                : definition.Effects;

            for (int effectIndex = 0; effectIndex < effects.Count; effectIndex++)
            {
                var effect = effects[effectIndex];
                if (string.IsNullOrEmpty(effect.Trigger)
                    || effect.Trigger != trigger)
                    continue;
                if (trigger == "OnCombo")
                {
                    if (comboCount is null) continue;
                    int minimum = effect.ComboMin ?? 1;
                    if (comboCount.Value < minimum) continue;
                }

                var source = CreateSource(
                    current, caster, card, effectIndex, effect);
                var (afterEffect, fired) = EffectApplier.Apply(
                    current, caster, effect, rng, catalog, source,
                    playingCardInstanceId: playingCardInstanceId);
                current = afterEffect;
                foreach (var battleEvent in fired)
                {
                    string suffix = deadEnemyInstanceId is null
                        ? string.Empty
                        : $";deadEnemy:{deadEnemyInstanceId}";
                    string prefix = $"power:{card.CardDefinitionId}{suffix}";
                    string note = string.IsNullOrEmpty(battleEvent.Note)
                        ? prefix
                        : $"{battleEvent.Note};{prefix}";
                    events.Add(battleEvent with
                    {
                        Order = order++,
                        Note = note,
                        Source = battleEvent.Source ?? source,
                    });
                }

                caster = FindLivingAlly(current, actorInstanceId);
                if (caster is null) break;
            }

            if (caster is null) break;
        }

        return (current, events);
    }

    private static CombatSource CreateSource(
        BattleState state, CombatActor caster, BattleCardInstance card,
        int effectIndex, CardEffect effect)
    {
        int? ownerSlot = caster.OwnerPlayerSlot ?? caster.SlotIndex;
        if (!state.Participants.IsDefaultOrEmpty
            && BattleParticipantAccess.TryGetByActor(
                state, caster.InstanceId, out var participant))
            ownerSlot = participant.OwnerPlayerSlot;

        return new CombatSource(
            CombatSourceKind.PowerEffect,
            OwnerSlot: ownerSlot,
            OwnerInstanceId: caster.InstanceId,
            ActorInstanceId: caster.InstanceId,
            DefinitionId: card.CardDefinitionId,
            InstanceId: card.InstanceId,
            EffectIndex: effectIndex,
            Scope: effect.Scope.ToString().ToLowerInvariant());
    }

    private static CardEffect TargetOnHitEffect(
        CardEffect effect, int enemyIndex) =>
        effect.Scope == EffectScope.Self
            || (effect.Action == "attack"
                && effect.Scope is EffectScope.Random or EffectScope.All)
            ? effect
            : effect with
            {
                Scope = EffectScope.Single,
                Side = enemyIndex >= 0
                    ? EffectSide.Enemy
                    : EffectSide.Ally,
            };

    private static bool PassesChance(CardEffect effect, IRng rng)
    {
        int chance = effect.ChancePercent ?? 100;
        return chance > 0
            && (chance >= 100 || rng.NextInt(0, 100) < chance);
    }

    private static string? ResolveSourceActorId(
        BattleState state, CombatSource source)
    {
        if (!string.IsNullOrWhiteSpace(source.ActorInstanceId))
        {
            string actorId = source.ActorInstanceId!;
            if (state.Participants.IsDefaultOrEmpty)
            {
                var hero = ResolveLegacyRootCaster(state);
                return hero is not null
                    && string.Equals(hero.InstanceId, actorId, StringComparison.Ordinal)
                    ? actorId
                    : null;
            }

            if (BattleParticipantAccess.TryGetByActor(state, actorId, out _))
                return actorId;
        }

        if (!string.IsNullOrWhiteSpace(source.OwnerInstanceId))
        {
            string ownerId = source.OwnerInstanceId!;
            if (!state.Participants.IsDefaultOrEmpty
                && BattleParticipantAccess.TryGetByActor(state, ownerId, out _))
                return ownerId;
            var hero = ResolveLegacyRootCaster(state);
            if (state.Participants.IsDefaultOrEmpty
                && hero is not null
                && string.Equals(hero.InstanceId, ownerId, StringComparison.Ordinal))
                return ownerId;
        }

        return state.Participants.IsDefaultOrEmpty
            ? ResolveLegacyRootCaster(state)?.InstanceId
            : null;
    }

    private static bool CanUseProjectedOwner(
        BattleState state, string actorInstanceId)
    {
        if (state.Participants.IsDefaultOrEmpty)
        {
            var hero = ResolveLegacyRootCaster(state);
            return hero is not null
                && string.Equals(
                    hero.InstanceId, actorInstanceId,
                    StringComparison.Ordinal);
        }

        return BattleParticipantAccess.TryGetByActor(
                state, actorInstanceId, out _)
            && IsLivingAlly(state, actorInstanceId);
    }

    private static CombatActor? ResolveLegacyRootCaster(BattleState state) =>
        ProvisionalSoloHeroOwnerResolver.Resolve(state)?.Hero;

    private static bool IsLivingAlly(
        BattleState state, string actorInstanceId) =>
        FindLivingAlly(state, actorInstanceId) is not null;

    private static CombatActor? FindLivingAlly(
        BattleState state, string actorInstanceId) =>
        state.Allies.FirstOrDefault(actor =>
            actor.IsAlive
            && string.Equals(
                actor.InstanceId, actorInstanceId,
                StringComparison.Ordinal));

    private static int FindIndex(
        ImmutableArray<CombatActor> actors, string instanceId)
    {
        for (int i = 0; i < actors.Length; i++)
            if (string.Equals(
                    actors[i].InstanceId, instanceId,
                    StringComparison.Ordinal))
                return i;
        return -1;
    }
}
