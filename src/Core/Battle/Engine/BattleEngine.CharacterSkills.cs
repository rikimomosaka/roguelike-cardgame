using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;

namespace RoguelikeCardGame.Core.Battle.Engine;

public static partial class BattleEngine
{
    public const int MaxComboGauge = 10;
    public const int TacticalSkillGaugeCost = 3;
    public const int UltimateSkillGaugeCost = 6;
    public const string TacticalSkillId = "tactical";
    public const string UltimateSkillId = "ultimate";

    private const bool TacticalSkillOncePerTurn = true;
    private const bool UltimateSkillOncePerTurn = false;

    /// <summary>戦技: ゲージ3を消費し、Strength+1とこのターンの全体攻撃化を得る。</summary>
    public static (BattleState, IReadOnlyList<BattleEvent>) UseTacticalSkill(
        BattleState state)
    {
        RejectIfParticipantReady(
            state, participantId: null, "UseTacticalSkill");
        var (next, events) = UseTacticalSkillForActor(
            state, casterActorInstanceId: null);
        if (!next.Participants.IsDefaultOrEmpty)
        {
            next = BattleParticipantAccess.CommitRootToParticipant(
                next, next.Participants[0].ParticipantId);
        }
        (next, events) = ApplyOutcomeAfterPlanningCommand(next, events);
        return (IncrementActionRevision(next), events);
    }

    public static (BattleState, IReadOnlyList<BattleEvent>) UseTacticalSkill(
        BattleState state,
        string participantId)
    {
        if (!BattleParticipantAccess.TryGet(state, participantId, out var participant))
            throw new InvalidOperationException(
                $"Battle participant '{participantId}' does not exist.");
        RejectIfParticipantReady(state, participantId, "UseTacticalSkill");
        var projected = BattleParticipantAccess.ProjectToRoot(state, participantId);
        var (processed, events) = UseTacticalSkillForActor(
            projected, participant.ActorInstanceId);
        var committed = BattleParticipantAccess.CommitRootToParticipant(
            processed, participantId);
        (committed, events) = ApplyOutcomeAfterPlanningCommand(
            committed, events);
        return (IncrementActionRevision(committed), events);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>)
        UseTacticalSkillForActor(
        BattleState state,
        string? casterActorInstanceId)
    {
        var (hero, heroIndex) = ValidateCharacterSkillUse(
            state, TacticalSkillId, TacticalSkillGaugeCost,
            requireCardsInHand: false,
            oncePerTurn: TacticalSkillOncePerTurn,
            casterActorInstanceId: casterActorInstanceId);
        var source = CharacterSkillSource(hero, TacticalSkillId);

        int currentStrength = hero.GetStatus("strength");
        int newStrength = currentStrength == int.MaxValue
            ? int.MaxValue
            : currentStrength + 1;
        int currentOmnistrike = hero.GetStatus("omnistrike");
        int newOmnistrike = currentOmnistrike == int.MaxValue
            ? int.MaxValue
            : currentOmnistrike + 1;

        var statuses = hero.Statuses;
        statuses = newStrength == 0
            ? statuses.Remove("strength")
            : statuses.SetItem("strength", newStrength);
        statuses = statuses.SetItem("omnistrike", newOmnistrike);

        var statusSources = hero.StatusSources;
        if (newStrength != currentStrength)
        {
            statusSources = newStrength == 0
                ? statusSources.Remove("strength")
                : statusSources.SetItem("strength", source);
        }
        if (newOmnistrike != currentOmnistrike)
            statusSources = statusSources.SetItem("omnistrike", source);

        var statusDisplayOrder = BattleStatusRules.NormalizeStatusDisplayOrder(hero);
        if (currentStrength == 0 && newStrength != 0)
            statusDisplayOrder = statusDisplayOrder.Remove("strength").Add("strength");
        if (currentOmnistrike == 0 && newOmnistrike != 0)
            statusDisplayOrder = statusDisplayOrder.Remove("omnistrike").Add("omnistrike");

        var updatedHero = hero with
        {
            Statuses = statuses,
            StatusSources = statusSources,
            StatusDisplayOrder = statusDisplayOrder,
        };
        int remainingGauge = state.ComboGauge - TacticalSkillGaugeCost;
        var tacticalUse = MarkCharacterSkillUsed(
            state, TacticalSkillId, TacticalSkillOncePerTurn ? 1 : 0);
        var next = state with
        {
            Allies = state.Allies.SetItem(heroIndex, updatedHero),
            ComboGauge = remainingGauge,
            UsedCharacterSkillIdsThisTurn = tacticalUse.UsedIds,
            CharacterSkillUsesThisTurn = tacticalUse.Counts,
        };

        var events = CharacterSkillEvents(
            hero, TacticalSkillId, TacticalSkillGaugeCost, remainingGauge, source).ToList();
        if (newStrength != currentStrength)
        {
            events.Add(new BattleEvent(
                newStrength == 0
                    ? BattleEventKind.RemoveStatus
                    : BattleEventKind.ApplyStatus,
                Order: events.Count,
                CasterInstanceId: hero.InstanceId,
                TargetInstanceId: hero.InstanceId,
                Amount: newStrength == 0 ? null : newStrength - currentStrength,
                Note: "strength",
                Source: source));
        }
        if (newOmnistrike != currentOmnistrike)
        {
            events.Add(new BattleEvent(
                BattleEventKind.ApplyStatus,
                Order: events.Count,
                CasterInstanceId: hero.InstanceId,
                TargetInstanceId: hero.InstanceId,
                Amount: newOmnistrike - currentOmnistrike,
                Note: "omnistrike",
                Source: source));
        }

        var appliedBuffs = new List<StatusChange>(capacity: 2);
        if (newStrength > currentStrength)
            appliedBuffs.Add(new StatusChange(
                BattleStatusIds.Strength,
                newStrength - currentStrength));
        if (newOmnistrike > currentOmnistrike)
            appliedBuffs.Add(new StatusChange(
                BattleStatusIds.Omnistrike,
                newOmnistrike - currentOmnistrike));
        int order = events.Count;
        next = PositiveBuffApplicationRules.ApplyResonance(
            next,
            hero.InstanceId,
            hero.GetStatus(BattleStatusIds.Resonance),
            appliedBuffs,
            source,
            events,
            ref order);

        return (next, events);
    }

    /// <summary>
    /// 奥義: 発動時点の手札へ、その時点のコンボ軽減前コストと同量の
    /// UntilTurnEnd軽減を付与する。
    /// </summary>
    public static (BattleState, IReadOnlyList<BattleEvent>) UseUltimateSkill(
        BattleState state, DataCatalog catalog)
    {
        RejectIfParticipantReady(
            state, participantId: null, "UseUltimateSkill");
        var (next, events) = UseUltimateSkillForActor(
            state, casterActorInstanceId: null, catalog: catalog);
        if (!next.Participants.IsDefaultOrEmpty)
        {
            next = BattleParticipantAccess.CommitRootToParticipant(
                next, next.Participants[0].ParticipantId);
        }
        (next, events) = ApplyOutcomeAfterPlanningCommand(next, events);
        return (IncrementActionRevision(next), events);
    }

    public static (BattleState, IReadOnlyList<BattleEvent>) UseUltimateSkill(
        BattleState state,
        string participantId,
        DataCatalog catalog)
    {
        if (!BattleParticipantAccess.TryGet(state, participantId, out var participant))
            throw new InvalidOperationException(
                $"Battle participant '{participantId}' does not exist.");
        RejectIfParticipantReady(state, participantId, "UseUltimateSkill");
        var projected = BattleParticipantAccess.ProjectToRoot(state, participantId);
        var (processed, events) = UseUltimateSkillForActor(
            projected, participant.ActorInstanceId, catalog);
        var committed = BattleParticipantAccess.CommitRootToParticipant(
            processed, participantId);
        (committed, events) = ApplyOutcomeAfterPlanningCommand(
            committed, events);
        return (IncrementActionRevision(committed), events);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>)
        UseUltimateSkillForActor(
        BattleState state,
        string? casterActorInstanceId,
        DataCatalog catalog)
    {
        var (hero, _) = ValidateCharacterSkillUse(
            state, UltimateSkillId, UltimateSkillGaugeCost,
            requireCardsInHand: false,
            oncePerTurn: UltimateSkillOncePerTurn,
            casterActorInstanceId: casterActorInstanceId);
        var source = CharacterSkillSource(hero, UltimateSkillId);
        var hand = state.Hand.ToBuilder();
        var modified = new List<(string InstanceId, long Delta)>();
        for (int i = 0; i < hand.Count; i++)
        {
            var card = hand[i];
            if (!catalog.TryGetCard(card.CardDefinitionId, out var definition))
                throw new InvalidOperationException(
                    $"card '{card.CardDefinitionId}' not in catalog");

            long? currentCost = BattleCardCostCalculator.GetRawCostBeforeCombo(
                card, definition, hero, isInHand: true);
            if (currentCost is null or <= 0) continue;

            long delta = -currentCost.Value;
            hand[i] = card.AddCostModifier(new CardCostModifier(
                delta,
                CardCostModifierDuration.UntilTurnEnd,
                SourceId: UltimateSkillId));
            modified.Add((card.InstanceId, delta));
        }
        int remainingGauge = state.ComboGauge - UltimateSkillGaugeCost;
        var ultimateUse = MarkCharacterSkillUsed(
            state, UltimateSkillId, UltimateSkillOncePerTurn ? 1 : 0);
        var next = state with
        {
            ComboGauge = remainingGauge,
            Hand = hand.ToImmutable(),
            UsedCharacterSkillIdsThisTurn = ultimateUse.UsedIds,
            CharacterSkillUsesThisTurn = ultimateUse.Counts,
        };

        var events = CharacterSkillEvents(
            hero, UltimateSkillId, UltimateSkillGaugeCost, remainingGauge, source).ToList();
        foreach (var change in modified)
        {
            events.Add(new BattleEvent(
                BattleEventKind.ModifyCardCost,
                Order: events.Count,
                CasterInstanceId: hero.InstanceId,
                TargetInstanceId: change.InstanceId,
                Amount: (int)Math.Clamp(change.Delta, int.MinValue, int.MaxValue),
                Note: "untilTurnEnd:hand",
                Source: source));
        }
        return (next, events);
    }

    private static (CombatActor Hero, int Index) ValidateCharacterSkillUse(
        BattleState state, string skillId, int requiredGauge,
        bool requireCardsInHand, bool oncePerTurn,
        string? casterActorInstanceId = null)
    {
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
        if (oncePerTurn && CharacterSkillUseCount(state, skillId) >= 1)
            throw new InvalidOperationException(
                $"Character skill '{skillId}' can only be used once per turn");
        if (state.ComboGauge < requiredGauge)
            throw new InvalidOperationException(
                $"insufficient combo gauge: have {state.ComboGauge}, need {requiredGauge}");
        if (requireCardsInHand && state.Hand.IsEmpty)
            throw new InvalidOperationException(
                "Cannot use ultimate skill with an empty hand");

        int actorIndex;
        CombatActor actor;
        if (casterActorInstanceId is null)
        {
            var owner = ProvisionalSoloHeroOwnerResolver.Resolve(state)
                ?? throw new InvalidOperationException(
                    "Character skill requires a living hero");
            actor = owner.Hero;
            actorIndex = owner.AllyIndex;
        }
        else
        {
            actorIndex = FindLivingParticipantCasterIndex(
                state, casterActorInstanceId);
            actor = state.Allies[actorIndex];
        }
        if (actor.GetStatus(BattleStatusIds.Seal) > 0)
            throw new InvalidOperationException(
                $"Character skill '{skillId}' is sealed");
        return (actor, actorIndex);
    }

    private static (ImmutableHashSet<string> UsedIds, ImmutableDictionary<string, int> Counts)
        MarkCharacterSkillUsed(BattleState state, string skillId, int usesPerTurn)
    {
        int count = CharacterSkillUseCount(state, skillId) + 1;
        var counts = state.CharacterSkillUsesThisTurn.SetItem(skillId, count);
        var used = usesPerTurn > 0
            ? state.UsedCharacterSkillIdsThisTurn.Add(skillId)
            : state.UsedCharacterSkillIdsThisTurn;
        return (used, counts);
    }

    private static int CharacterSkillUseCount(BattleState state, string skillId) =>
        state.CharacterSkillUsesThisTurn.TryGetValue(skillId, out var count)
            ? count
            : state.UsedCharacterSkillIdsThisTurn.Contains(skillId) ? 1 : 0;

    private static void ValidateComboTrackingState(BattleState state)
    {
        if (state.ComboCount < 0)
            throw new InvalidOperationException(
                $"ComboCount must be non-negative, got {state.ComboCount}");
        if (state.MaxComboThisTurn < state.ComboCount)
            throw new InvalidOperationException(
                "MaxComboThisTurn must be greater than or equal to ComboCount: " +
                $"max={state.MaxComboThisTurn}, current={state.ComboCount}");
        if (state.ComboGauge is < 0 or > MaxComboGauge)
            throw new InvalidOperationException(
                $"ComboGauge must be between 0 and {MaxComboGauge}, got {state.ComboGauge}");
    }

    private static CombatSource CharacterSkillSource(CombatActor hero, string skillId) =>
        new(
            CombatSourceKind.CharacterSkill,
            OwnerSlot: hero.OwnerPlayerSlot ?? hero.SlotIndex,
            OwnerInstanceId: hero.OwnerActorInstanceId ?? hero.InstanceId,
            ActorInstanceId: hero.InstanceId,
            DefinitionId: skillId,
            Scope: "self");

    private static IReadOnlyList<BattleEvent> CharacterSkillEvents(
        CombatActor hero, string skillId, int cost, int remainingGauge,
        CombatSource source) =>
        new BattleEvent[]
        {
            new(
                BattleEventKind.UseCharacterSkill,
                Order: 0,
                CasterInstanceId: hero.InstanceId,
                Amount: cost,
                Note: skillId,
                Source: source),
            new(
                BattleEventKind.ComboGaugeChanged,
                Order: 1,
                CasterInstanceId: hero.InstanceId,
                Amount: -cost,
                Note: $"remaining={remainingGauge}",
                Source: source),
        };
}
