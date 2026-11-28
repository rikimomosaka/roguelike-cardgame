using System;
using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

public static partial class BattleEngine
{
    /// <summary>
    /// Resolves a character skill from the active character definition.  Skill
    /// effects deliberately use the same CardEffect pipeline as cards, so a
    /// new character does not require a new engine branch.
    /// </summary>
    public static (BattleState, IReadOnlyList<BattleEvent>) UseCharacterSkill(
        BattleState state,
        string skillId,
        DataCatalog catalog,
        IRng rng)
    {
        RejectIfParticipantReady(
            state, participantId: null, "UseCharacterSkill");
        var (next, events) = UseCharacterSkillForActor(
            state, casterActorInstanceId: null, skillId, catalog, rng);
        if (!next.Participants.IsDefaultOrEmpty)
        {
            next = BattleParticipantAccess.CommitRootToParticipant(
                next, next.Participants[0].ParticipantId);
        }
        (next, events) = ApplyOutcomeAfterPlanningCommand(next, events);
        return (IncrementActionRevision(next), events);
    }

    public static (BattleState, IReadOnlyList<BattleEvent>) UseCharacterSkill(
        BattleState state,
        string participantId,
        string skillId,
        DataCatalog catalog,
        IRng rng)
    {
        if (!BattleParticipantAccess.TryGet(state, participantId, out var participant))
            throw new InvalidOperationException(
                $"Battle participant '{participantId}' does not exist.");
        RejectIfParticipantReady(state, participantId, "UseCharacterSkill");
        var projected = BattleParticipantAccess.ProjectToRoot(state, participantId);
        var (processed, events) = UseCharacterSkillForActor(
            projected, participant.ActorInstanceId, skillId, catalog, rng);
        var committed = BattleParticipantAccess.CommitRootToParticipant(
            processed, participantId);
        (committed, events) = ApplyOutcomeAfterPlanningCommand(
            committed, events);
        return (IncrementActionRevision(committed), events);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>)
        UseCharacterSkillForActor(
        BattleState state,
        string? casterActorInstanceId,
        string skillId,
        DataCatalog catalog,
        IRng rng)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(rng);
        ValidateComboTrackingState(state);

        if (state.Outcome != RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending)
            throw new InvalidOperationException(
                $"Character skill requires Outcome=Pending, got {state.Outcome}");
        if (state.Phase != BattlePhase.PlayerInput)
            throw new InvalidOperationException(
                $"Character skill requires Phase=PlayerInput, got {state.Phase}");
        if (state.PendingCardPlay is not null)
            throw new InvalidOperationException(
                "Cannot use character skill while PendingCardPlay is set");
        IReadOnlyList<CharacterSkillDefinition> availableSkills;
        string abilityOwnerKind;
        if (catalog.TryGetCharacter(state.CharacterId, out var character))
        {
            availableSkills = character.EffectiveSkills;
            abilityOwnerKind = "Character";
        }
        else if (catalog.TryGetUnit(state.CharacterId, out var unit))
        {
            availableSkills = unit.EffectiveSkills;
            abilityOwnerKind = "Unit";
        }
        else
        {
            throw new InvalidOperationException(
                $"Character or unit '{state.CharacterId}' not found in catalog");
        }
        var skill = availableSkills.FirstOrDefault(s =>
            string.Equals(s.Id, skillId, StringComparison.Ordinal));
        if (skill is null)
            throw new InvalidOperationException(
                $"{abilityOwnerKind} '{state.CharacterId}' does not define skill '{skillId}'");
        if (!skill.Cost.IsValid)
            throw new InvalidOperationException(
                $"Character skill '{skill.Id}' has an invalid resource cost");
        int uses = CharacterSkillUseCount(state, skill.Id);
        if (skill.EffectiveUsesPerTurn > 0 && uses >= skill.EffectiveUsesPerTurn)
            throw new InvalidOperationException(
                $"Character skill '{skill.Id}' can only be used {skill.EffectiveUsesPerTurn} time(s) per turn");
        if (state.Energy < skill.Cost.Energy)
            throw new InvalidOperationException(
                $"insufficient energy: have {state.Energy}, need {skill.Cost.Energy}");
        if (state.ComboGauge < skill.Cost.ComboGauge)
            throw new InvalidOperationException(
                $"insufficient combo gauge: have {state.ComboGauge}, need {skill.Cost.ComboGauge}");
        var hero = casterActorInstanceId is null
            ? ProvisionalSoloHeroOwnerResolver.Resolve(state)?.Hero
            : state.Allies.FirstOrDefault(actor =>
                actor.IsAlive
                && string.Equals(
                    actor.InstanceId,
                    casterActorInstanceId,
                    StringComparison.Ordinal));
        if (hero is null)
            throw new InvalidOperationException(
                "Character skill requires a living participant actor");
        if (hero.GetStatus(BattleStatusIds.Seal) > 0)
            throw new InvalidOperationException(
                $"Character skill '{skill.Id}' is sealed");
        if (skill.RequiresPhase && hero.Phase == ActorPhase.None)
            throw new InvalidOperationException(
                $"Character skill '{skill.Id}' requires a phase (calm or motion)");
        // 位相ごとの効果は、使用を始めた時点の位相で選ぶ。
        var skillEffects = skill.EffectsForPhase(PhaseRules.Name(hero.Phase));
        var source = CharacterSkillSource(hero, skill.Id);
        var remainingGauge = state.ComboGauge - skill.Cost.ComboGauge;
        var useState = MarkCharacterSkillUsed(state, skill.Id, skill.EffectiveUsesPerTurn);
        var next = state with
        {
            Energy = state.Energy - skill.Cost.Energy,
            ComboGauge = remainingGauge,
            UsedCharacterSkillIdsThisTurn = useState.UsedIds,
            CharacterSkillUsesThisTurn = useState.Counts,
        };
        var events = CharacterSkillEvents(
            hero, skill.Id, skill.Cost.ComboGauge, remainingGauge, source).ToList();

        int order = events.Count;
        foreach (var effect in skillEffects)
        {
            if (!string.IsNullOrEmpty(effect.Trigger))
                continue;
            var currentOwner = next.Allies.FirstOrDefault(actor =>
                actor.IsAlive
                && string.Equals(
                    actor.InstanceId,
                    hero.InstanceId,
                    StringComparison.Ordinal))
                ?? throw new InvalidOperationException(
                    $"Character skill '{skill.Id}' lost its living actor during resolution");
            var (after, effectEvents) = EffectApplier.Apply(
                next, currentOwner, effect, rng, catalog,
                source: source);
            next = after;
            foreach (var effectEvent in effectEvents)
                events.Add(effectEvent with { Order = order++ });
        }

        return (next, events);
    }
}
