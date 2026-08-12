using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Data;

namespace RoguelikeCardGame.Core.Battle.Engine;

public static partial class BattleEngine
{
    public const int MaxComboGauge = 10;
    public const int TacticalSkillGaugeCost = 3;
    public const int UltimateSkillGaugeCost = 10;
    public const string TacticalSkillId = "tactical";
    public const string UltimateSkillId = "ultimate";

    private const bool TacticalSkillOncePerTurn = true;
    private const bool UltimateSkillOncePerTurn = false;

    /// <summary>戦技: ゲージ3を消費し、Strength+1とこのターンの全体攻撃化を得る。</summary>
    public static (BattleState, IReadOnlyList<BattleEvent>) UseTacticalSkill(
        BattleState state)
    {
        var (hero, heroIndex) = ValidateCharacterSkillUse(
            state, TacticalSkillId, TacticalSkillGaugeCost,
            requireCardsInHand: false,
            oncePerTurn: TacticalSkillOncePerTurn);
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

        var updatedHero = hero with
        {
            Statuses = statuses,
            StatusSources = statusSources,
        };
        int remainingGauge = state.ComboGauge - TacticalSkillGaugeCost;
        var next = state with
        {
            Allies = state.Allies.SetItem(heroIndex, updatedHero),
            ComboGauge = remainingGauge,
            UsedCharacterSkillIdsThisTurn = MarkCharacterSkillUsed(
                state, TacticalSkillId, TacticalSkillOncePerTurn),
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

        return (next, events);
    }

    /// <summary>
    /// 奥義: 発動時点の手札へ、その時点のコンボ軽減前コストと同量の
    /// UntilTurnEnd軽減を付与する。
    /// </summary>
    public static (BattleState, IReadOnlyList<BattleEvent>) UseUltimateSkill(
        BattleState state, DataCatalog catalog)
    {
        var (hero, _) = ValidateCharacterSkillUse(
            state, UltimateSkillId, UltimateSkillGaugeCost,
            requireCardsInHand: true,
            oncePerTurn: UltimateSkillOncePerTurn);
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
        var next = state with
        {
            ComboGauge = remainingGauge,
            Hand = hand.ToImmutable(),
            UsedCharacterSkillIdsThisTurn = MarkCharacterSkillUsed(
                state, UltimateSkillId, UltimateSkillOncePerTurn),
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
        bool requireCardsInHand, bool oncePerTurn)
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
        if (oncePerTurn && state.UsedCharacterSkillIdsThisTurn.Contains(skillId))
            throw new InvalidOperationException(
                $"Character skill '{skillId}' can only be used once per turn");
        if (state.ComboGauge < requiredGauge)
            throw new InvalidOperationException(
                $"insufficient combo gauge: have {state.ComboGauge}, need {requiredGauge}");
        if (requireCardsInHand && state.Hand.IsEmpty)
            throw new InvalidOperationException(
                "Cannot use ultimate skill with an empty hand");

        var owner = ProvisionalSoloHeroOwnerResolver.Resolve(state);
        return owner is not null
            ? (owner.Hero, owner.AllyIndex)
            : throw new InvalidOperationException(
                "Character skill requires a living hero");
    }

    private static ImmutableHashSet<string> MarkCharacterSkillUsed(
        BattleState state, string skillId, bool oncePerTurn) =>
        oncePerTurn
            ? state.UsedCharacterSkillIdsThisTurn.Add(skillId)
            : state.UsedCharacterSkillIdsThisTurn;

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
