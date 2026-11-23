using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Battle.Projection;

/// <summary>
/// Deterministic preview of the attacks that Residual/Afterimage will replay at
/// an actor's attack boundary.  The preview never consumes RNG or mutates the
/// authoritative battle. Enemy mitigation, death and reaction effects are
/// deliberately outside this attacker-side intent.
///
/// The dry run models deterministic caster-side changes that can alter an
/// existing amountSource or the final attack adjustment: Block, healing,
/// self-damage, Energy and status changes. Effects involving a player choice,
/// a non-unique random caster target, or an amount that depends on a card pile
/// after that pile has changed are not guessed. A pile change by itself does
/// not hide otherwise deterministic attacks. Relic/power/trait reactions are
/// outside this catalog-free projection.
/// </summary>
public static class AfterimageIntentProjector
{
    public static CombatIntentProjection? Project(
        BattleState state,
        CombatActor actor) => Analyze(state, actor).Intent;

    /// <summary>
    /// Returns both the deterministic intent and whether a missing intent means
    /// "no attack" or "cannot be projected". Transports can therefore explain
    /// unavailable previews without duplicating projection rules.
    /// </summary>
    public static AfterimageIntentProjectionResult Analyze(
        BattleState state,
        CombatActor actor)
    {
        int stacks = actor.GetStatus(BattleStatusIds.Afterimage);
        if (stacks <= 0
            || !actor.IsAlive
            || !BattleParticipantAccess.TryGetByActor(
                state, actor.InstanceId, out var participant)
            || participant.LastFinisherRecord.IsDefaultOrEmpty)
        {
            return new AfterimageIntentProjectionResult(
                AfterimageIntentProjectionAvailability.NotApplicable,
                Intent: null);
        }

        var projected = state.Participants.IsDefaultOrEmpty
            ? state
            : BattleParticipantAccess.ProjectToRoot(
                state, participant.ParticipantId);
        var virtualActor = FindActor(projected, actor.InstanceId) ?? actor;
        int single = 0;
        int random = 0;
        int all = 0;
        int singleHits = 0;
        int randomHits = 0;
        int allHits = 0;
        bool pileCountsAreUnknown = false;

        for (int cycle = 0; cycle < stacks; cycle++)
        {
            foreach (var snapshot in participant.LastFinisherRecord)
            {
                var attacks = ImmutableArray.CreateBuilder<CardEffect>();
                foreach (var effect in snapshot.Effects)
                {
                    if (!string.IsNullOrEmpty(effect.Trigger)
                        || effect.Action is "exhaustSelf" or "retainSelf"
                        || effect.ComboMin is { } comboMin
                            && projected.ComboCount < comboMin
                        || effect.MaxComboMin is { } maxComboMin
                            && projected.MaxComboThisTurn < maxComboMin)
                    {
                        continue;
                    }

                    if (MutatesCardPiles(effect.Action))
                    {
                        // Which cards move can be random or player-selected. This
                        // only matters to the preview once a later effect reads a
                        // pile count; fixed attacks remain safe to show.
                        pileCountsAreUnknown = true;
                        continue;
                    }

                    if (pileCountsAreUnknown
                        && IsCardPileAmountSource(effect.AmountSource))
                    {
                        return new AfterimageIntentProjectionResult(
                            AfterimageIntentProjectionAvailability.Unavailable,
                            Intent: null);
                    }

                    int amount = ResolveAmount(
                        effect, projected, virtualActor);
                    var resolved = effect with { Amount = amount };
                    if (resolved.Action == "attack")
                    {
                        attacks.Add(resolved);
                        continue;
                    }

                    if (!TryApplyDeterministicSelfEffect(
                            projected,
                            virtualActor,
                            resolved,
                            out projected,
                            out virtualActor))
                    {
                        return new AfterimageIntentProjectionResult(
                            AfterimageIntentProjectionAvailability.Unavailable,
                            Intent: null);
                    }
                    projected = ReplaceActor(
                        projected, virtualActor.InstanceId, virtualActor);
                    // Runtime still walks the remainder of this snapshot, but
                    // ResolveAddedAttacks rejects a dead caster and the outer
                    // replay stops before the next saved card. No queued attack
                    // from this snapshot can therefore contribute to intent.
                    if (!virtualActor.IsAlive)
                    {
                        attacks.Clear();
                        break;
                    }
                }

                if (!virtualActor.IsAlive)
                    break;

                int strength = virtualActor.GetStatus(BattleStatusIds.Strength);
                int weak = virtualActor.GetStatus(BattleStatusIds.Weak);
                int vigor = virtualActor.GetStatus(BattleStatusIds.Vigor);
                int omnistrike = virtualActor.GetStatus(BattleStatusIds.Omnistrike);
                int disruption = virtualActor.GetStatus(BattleStatusIds.Disruption);
                foreach (var attack in attacks)
                {
                    int adjusted = AttackDamageMath.AdjustForAttacker(
                        attack.Amount, strength, weak, vigor);
                    switch (AttackScopeRules.Resolve(
                        attack.Scope, omnistrike, disruption))
                    {
                        case EffectScope.Single:
                            single = unchecked(single + adjusted);
                            singleHits++;
                            break;
                        case EffectScope.Random:
                            random = unchecked(random + adjusted);
                            randomHits++;
                            break;
                        case EffectScope.All:
                            all = unchecked(all + adjusted);
                            allHits++;
                            break;
                    }
                }
            }

            if (!virtualActor.IsAlive)
                break;
        }

        int totalHits = singleHits + randomHits + allHits;
        if (totalHits == 0)
        {
            return new AfterimageIntentProjectionResult(
                AfterimageIntentProjectionAvailability.Available,
                Intent: null);
        }
        return new AfterimageIntentProjectionResult(
            AfterimageIntentProjectionAvailability.Available,
            new CombatIntentProjection(
            AttackSingle: singleHits > 0 ? single : null,
            AttackRandom: randomHits > 0 ? random : null,
            AttackAll: allHits > 0 ? all : null,
            AttackHits: totalHits,
            AttackSingleHits: singleHits > 0 ? singleHits : null,
            AttackRandomHits: randomHits > 0 ? randomHits : null,
            AttackAllHits: allHits > 0 ? allHits : null,
            Block: null,
            HasBuff: false,
            HasDebuff: false,
            HasHeal: false));
    }

    private static int ResolveAmount(
        CardEffect effect,
        BattleState state,
        CombatActor actor) => string.IsNullOrEmpty(effect.AmountSource)
            ? effect.Amount
            : AmountSourceEvaluator.Evaluate(effect.AmountSource, state, actor);

    private static bool TryApplyDeterministicSelfEffect(
        BattleState state,
        CombatActor actor,
        CardEffect effect,
        out BattleState projectedState,
        out CombatActor projectedActor)
    {
        projectedState = state;
        projectedActor = actor;

        if (!string.IsNullOrEmpty(effect.Select)
            || MutatesCardPiles(effect.Action))
        {
            return false;
        }

        var targetRelation = ActorTargetRelationFor(state, actor, effect);
        if (targetRelation == ActorTargetRelation.Maybe)
            return !CanChangeProjectedCaster(effect.Action);
        if (targetRelation == ActorTargetRelation.None
            && effect.Action != "selfDamage")
        {
            return true;
        }

        if (effect.Action == "block")
        {
            projectedActor = actor with
            {
                Block = actor.Block.Add(effect.Amount),
            };
            return true;
        }

        if (effect.Action == "heal")
        {
            int actualHeal = Math.Min(
                effect.Amount,
                actor.MaxHp - actor.CurrentHp);
            if (actor.IsAlive && actualHeal > 0)
            {
                projectedActor = actor with
                {
                    CurrentHp = actor.CurrentHp + actualHeal,
                };
            }
            return true;
        }

        if (effect.Action == "selfDamage")
        {
            if (effect.Scope != EffectScope.Self)
                return false;

            projectedActor = actor with
            {
                CurrentHp = Math.Max(
                    0,
                    actor.CurrentHp - Math.Max(0, effect.Amount)),
            };
            BattleStatusRules.TryRevive(projectedActor, out var afterRevive);
            projectedActor = afterRevive;
            return true;
        }

        if (effect.Action == "gainEnergy")
        {
            projectedState = state with
            {
                Energy = EnergyMath.AddSaturating(
                    state.Energy,
                    effect.Amount),
            };
            return true;
        }

        if (effect.Action is not ("buff" or "debuff")) return true;

        var changes = effect.EffectiveStatusChanges();
        bool harmful = changes.Any(change =>
            IsHarmfulStatusChange(effect.Action, change));
        if (harmful && actor.GetStatus(BattleStatusIds.Resistance) > 0)
        {
            projectedActor = BattleStatusRules.Decrement(
                actor,
                BattleStatusIds.Resistance);
            return true;
        }

        var statuses = actor.Statuses;
        foreach (var change in changes)
        {
            int current = projectedActor.GetStatus(change.Name);
            bool positiveAfterglow = string.Equals(
                    change.Name,
                    BattleStatusIds.Afterglow,
                    StringComparison.Ordinal)
                && change.Amount > 0;
            int value = positiveAfterglow
                ? 1
                : (int)Math.Clamp(
                    (long)current + change.Amount,
                    int.MinValue,
                    int.MaxValue);
            bool signed = IsSignedStatus(change.Name);
            if ((!signed && value <= 0) || value == 0)
                statuses = statuses.Remove(change.Name);
            else
                statuses = statuses.SetItem(change.Name, value);
            projectedActor = projectedActor with { Statuses = statuses };
        }
        return true;
    }

    private static bool IsHarmfulStatusChange(
        string action,
        StatusChange change) =>
        StatusDefinition.Get(change.Name).Kind == StatusKind.Debuff
            && change.Amount > 0
        || action == "debuff"
            && IsSignedStatus(change.Name)
            && change.Amount < 0;

    private static bool IsSignedStatus(string name) =>
        name is BattleStatusIds.Strength or BattleStatusIds.Dexterity;

    private static bool MutatesCardPiles(string action) => action is
        "draw" or "drawCards" or "discard" or "exhaustCard"
        or "addCard" or "recoverFromDiscard" or "recoverFromExhaust";

    private static bool IsCardPileAmountSource(string? source) => source is
        "handCount" or "drawPileCount" or "discardPileCount"
        or "exhaustPileCount";

    private static bool CanChangeProjectedCaster(string action) => action is
        "block" or "heal" or "buff" or "debuff" or "gainEnergy";

    private static ActorTargetRelation ActorTargetRelationFor(
        BattleState state,
        CombatActor actor,
        CardEffect effect)
    {
        if (effect.Scope == EffectScope.Self)
            return ActorTargetRelation.Definitely;
        if (effect.Side != EffectSide.Ally)
            return ActorTargetRelation.None;
        var ownSide = actor.Side == ActorSide.Ally ? state.Allies : state.Enemies;
        return effect.Scope switch
        {
            EffectScope.All => actor.IsAlive
                ? ActorTargetRelation.Definitely
                : ActorTargetRelation.None,
            EffectScope.Random => !actor.IsAlive
                ? ActorTargetRelation.None
                : ownSide.Count(candidate => candidate.IsAlive) == 1
                    ? ActorTargetRelation.Definitely
                    : ActorTargetRelation.Maybe,
            EffectScope.Single => SelectedOwnSideActor(state, actor.Side)?.InstanceId
                == actor.InstanceId
                    ? ActorTargetRelation.Definitely
                    : ActorTargetRelation.None,
            _ => ActorTargetRelation.None,
        };
    }

    private enum ActorTargetRelation
    {
        None,
        Definitely,
        Maybe,
    }

    private static CombatActor? SelectedOwnSideActor(
        BattleState state,
        ActorSide side)
    {
        if (side == ActorSide.Ally)
        {
            return state.TargetAllyIndex is { } index
                && index >= 0
                && index < state.Allies.Length
                    ? state.Allies[index]
                    : null;
        }
        return state.TargetEnemyIndex is { } enemyIndex
            && enemyIndex >= 0
            && enemyIndex < state.Enemies.Length
                ? state.Enemies[enemyIndex]
                : null;
    }

    private static CombatActor? FindActor(BattleState state, string instanceId) =>
        state.Allies.Concat(state.Enemies).FirstOrDefault(candidate =>
            string.Equals(candidate.InstanceId, instanceId, StringComparison.Ordinal));

    private static BattleState ReplaceActor(
        BattleState state,
        string instanceId,
        CombatActor replacement)
    {
        int allyIndex = -1;
        for (int index = 0; index < state.Allies.Length; index++)
        {
            if (string.Equals(
                    state.Allies[index].InstanceId,
                    instanceId,
                    StringComparison.Ordinal))
            {
                allyIndex = index;
                break;
            }
        }
        if (allyIndex >= 0)
            return state with { Allies = state.Allies.SetItem(allyIndex, replacement) };
        int enemyIndex = -1;
        for (int index = 0; index < state.Enemies.Length; index++)
        {
            if (string.Equals(
                    state.Enemies[index].InstanceId,
                    instanceId,
                    StringComparison.Ordinal))
            {
                enemyIndex = index;
                break;
            }
        }
        return enemyIndex >= 0
            ? state with { Enemies = state.Enemies.SetItem(enemyIndex, replacement) }
            : state;
    }
}

public enum AfterimageIntentProjectionAvailability
{
    NotApplicable,
    Available,
    Unavailable,
}

public sealed record AfterimageIntentProjectionResult(
    AfterimageIntentProjectionAvailability Availability,
    CombatIntentProjection? Intent);
