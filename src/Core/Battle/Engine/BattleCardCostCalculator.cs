using System;
using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// カードの元コスト、個体modifier、手札中だけ有効な主人公status、コンボ軽減を
/// 一つの順序で評価する Core の唯一のコスト計算器。
/// </summary>
public static class BattleCardCostCalculator
{
    public const string HandCostIncreaseStatusId = "handCostIncrease";
    public const string HandCostReductionStatusId = "handCostReduction";

    public static int? GetOriginalCost(
        BattleCardInstance card, CardDefinition definition) =>
        card.IsUpgraded
            ? definition.UpgradedCost ?? definition.Cost
            : definition.Cost;

    /// <summary>
    /// コンボ軽減前の現在コスト。Ultimate のsnapshot量もこの値を使う。
    /// </summary>
    public static int? GetCostBeforeCombo(
        BattleCardInstance card,
        CardDefinition definition,
        CombatActor? owner,
        bool isInHand)
    {
        long? raw = GetRawCostBeforeCombo(card, definition, owner, isInHand);
        return raw is null ? null : ClampCost(raw.Value);
    }

    /// <summary>
    /// 最終表示範囲へ丸める前の現在コスト。コンボ軽減をこの値へ適用してから
    /// 最後にclampすることで、int上限付近でも計算順を維持する。
    /// </summary>
    public static long? GetRawCostBeforeCombo(
        BattleCardInstance card,
        CardDefinition definition,
        CombatActor? owner,
        bool isInHand)
    {
        int? original = GetOriginalCost(card, definition);
        if (original is null) return null;

        long value = card.CostOverride ?? original.Value;
        foreach (var modifier in card.EffectiveCostModifiers)
            value = AddSaturating(value, modifier.Amount);

        if (isInHand && owner is not null)
        {
            value = AddSaturating(
                value, Math.Max(0, owner.GetStatus(HandCostIncreaseStatusId)));
            value = AddSaturating(
                value, -Math.Max(0L, owner.GetStatus(HandCostReductionStatusId)));
        }

        return value;
    }

    public static CardCostBreakdown Evaluate(
        BattleCardInstance card,
        CardDefinition definition,
        CombatActor? owner,
        bool isInHand,
        bool comboReductionEligible)
    {
        int? original = GetOriginalCost(card, definition);
        long? rawBeforeCombo = GetRawCostBeforeCombo(card, definition, owner, isInHand);
        int? beforeCombo = rawBeforeCombo is null
            ? null
            : ClampCost(rawBeforeCombo.Value);
        int? payable = rawBeforeCombo is null
            ? null
            : ClampCost(AddSaturating(
                rawBeforeCombo.Value, comboReductionEligible ? -1L : 0L));

        var changes = ImmutableArray.CreateBuilder<CardCostChangeStep>();
        if (original is not null)
        {
            if (card.CostOverride is { } fixedCost)
            {
                changes.Add(new CardCostChangeStep(
                    CardCostChangeKind.Fixed,
                    fixedCost,
                    BattleStatusIds.Confusion));
            }

            foreach (var modifier in card.EffectiveCostModifiers)
            {
                changes.Add(new CardCostChangeStep(
                    CardCostChangeKind.Modifier,
                    modifier.Amount,
                    modifier.SourceId));
            }

            if (isInHand && owner is not null)
            {
                int increase = Math.Max(0, owner.GetStatus(HandCostIncreaseStatusId));
                if (increase > 0)
                {
                    changes.Add(new CardCostChangeStep(
                        CardCostChangeKind.Status,
                        increase,
                        HandCostIncreaseStatusId));
                }

                int reduction = Math.Max(0, owner.GetStatus(HandCostReductionStatusId));
                if (reduction > 0)
                {
                    changes.Add(new CardCostChangeStep(
                        CardCostChangeKind.Status,
                        -(long)reduction,
                        HandCostReductionStatusId));
                }
            }

            if (comboReductionEligible)
            {
                changes.Add(new CardCostChangeStep(
                    CardCostChangeKind.Combo,
                    -1));
            }
        }

        return new CardCostBreakdown(
            OriginalCost: original,
            CostBeforeCombo: beforeCombo,
            PayableCost: payable,
            ComboReductionEligible: comboReductionEligible && beforeCombo is not null)
        {
            Changes = changes.ToImmutable(),
        };
    }

    /// <summary>
    /// 現在のコンボ状態だけを使って、次にこのカードを手札から使う際の
    /// 1コスト軽減対象かを判定する。表示と実際の支払いで同じ規則を使うための公開境界。
    /// </summary>
    public static bool IsComboReductionEligible(
        BattleState state,
        BattleCardInstance card,
        CardDefinition definition)
    {
        return IsNormalComboSuccessor(state, card, definition)
            || state.NextCardComboFreePass
            || UsesContinuity(state, card, definition);
    }

    /// <summary>
    /// Participant-aware form used when the projected hand belongs to an ally
    /// other than the provisional solo hero.
    /// </summary>
    public static bool IsComboReductionEligible(
        BattleState state,
        BattleCardInstance card,
        CardDefinition definition,
        CombatActor owner)
    {
        return IsNormalComboSuccessor(state, card, definition)
            || state.NextCardComboFreePass
            || UsesContinuity(state, card, definition, owner);
    }

    /// <summary>今回のカードを使った結果、コンボ自体が継続するか。</summary>
    public static bool IsComboContinuation(
        BattleState state,
        BattleCardInstance card,
        CardDefinition definition)
        => state.ComboCarryoverPending
            || IsComboContinuationWithoutContinuity(state, card, definition)
            || UsesContinuity(state, card, definition);

    public static bool IsComboContinuation(
        BattleState state,
        BattleCardInstance card,
        CardDefinition definition,
        CombatActor owner)
        => state.ComboCarryoverPending
            || IsComboContinuationWithoutContinuity(state, card, definition)
            || UsesContinuity(state, card, definition, owner);

    /// <summary>
    /// True when this card continues the combo without spending Continuity.
    /// This is the authoritative rule for the yellow cost ring.
    /// </summary>
    public static bool IsComboContinuationWithoutContinuity(
        BattleState state,
        BattleCardInstance card,
        CardDefinition definition)
    {
        bool matchesNormal = IsNormalComboSuccessor(state, card, definition);
        var keywords = definition.EffectiveKeywords(card.IsUpgraded);
        bool hasWild = keywords?.Contains("wild") == true;
        bool hasSuperWild = keywords?.Contains("superwild") == true;
        bool wildEffective = hasWild && !hasSuperWild && !state.WildUsedInCurrentCombo;
        // A Wild/SuperWild starts a combo, but is not itself a continuation
        // when it is the first card of the turn/combo.
        bool comboAlreadyStarted = state.LastPlayedOrigCost is not null;
        return state.NextCardComboFreePass
            || matchesNormal
            || (comboAlreadyStarted && (hasSuperWild || wildEffective));
    }

    public static bool UsesContinuity(
        BattleState state,
        BattleCardInstance card,
        CardDefinition definition)
    {
        var owner = ProvisionalSoloHeroOwnerResolver.Resolve(state)?.Hero;
        return UsesContinuity(state, card, definition, owner);
    }

    public static bool UsesContinuity(
        BattleState state,
        BattleCardInstance card,
        CardDefinition definition,
        CombatActor? owner)
    {
        if (state.LastPlayedOrigCost is null)
            return false;
        if (state.NextCardComboFreePass
            || IsNormalComboSuccessor(state, card, definition))
            return false;

        var keywords = definition.EffectiveKeywords(card.IsUpgraded);
        bool hasSuperWild = keywords?.Contains("superwild") == true;
        bool wildEffective = keywords?.Contains("wild") == true
            && !hasSuperWild
            && !state.WildUsedInCurrentCombo;
        if (hasSuperWild || wildEffective)
            return false;

        return owner?.GetStatus(BattleStatusIds.Continuity) > 0;
    }

    public static bool IsNormalComboSuccessor(
        BattleState state,
        BattleCardInstance card,
        CardDefinition definition)
    {
        int? original = GetOriginalCost(card, definition);
        return original is not null
            && state.LastPlayedOrigCost is { } previous
            && (long)original.Value == (long)previous + 1L;
    }

    /// <summary>
    /// Returns the status id that currently prevents this card from being used.
    /// Kept in Core so Server projections cannot drift from PlayCard.
    /// </summary>
    public static string? GetPlayRestrictionReason(
        BattleState state,
        CombatActor owner,
        CardDefinition definition)
    {
        if (state.CardUseLockedThisTurn)
            return BattleStatusIds.Curse;
        if (owner.GetStatus(BattleStatusIds.Bind) > 0
            && definition.CardType == CardType.Attack)
            return BattleStatusIds.Bind;
        if (owner.GetStatus(BattleStatusIds.Silence) > 0
            && definition.CardType != CardType.Attack)
            return BattleStatusIds.Silence;
        return null;
    }

    /// <summary>手札表示と PlayCard が共有する、現在支払うコストの完全な投影。</summary>
    public static CardCostBreakdown EvaluateForHand(
        BattleState state,
        BattleCardInstance card,
        CardDefinition definition,
        CombatActor owner)
    {
        var result = Evaluate(
            card,
            definition,
            owner,
            isInHand: true,
            comboReductionEligible: IsComboReductionEligible(
                state, card, definition, owner));
        return result with
        {
            ComboContinues = IsComboContinuation(
                state, card, definition, owner),
            ComboRingHighlighted = IsComboContinuationWithoutContinuity(
                state, card, definition),
        };
    }

    private static int ClampCost(long value) =>
        (int)Math.Clamp(value, 0L, int.MaxValue);

    private static long AddSaturating(long left, long right)
    {
        if (right > 0 && left > long.MaxValue - right) return long.MaxValue;
        if (right < 0 && left < long.MinValue - right) return long.MinValue;
        return left + right;
    }
}

public sealed record CardCostBreakdown(
    int? OriginalCost,
    int? CostBeforeCombo,
    int? PayableCost,
    bool ComboReductionEligible)
{
    public ImmutableArray<CardCostChangeStep> Changes { get; init; } = [];
    public bool ComboContinues { get; init; }
    public bool ComboRingHighlighted { get; init; }
    public bool DiffersFromOriginal => OriginalCost != PayableCost;
}

/// <summary>
/// One authoritative, ordered step in a card's payable-cost calculation.
/// Fixed values replace the running value; all other values are signed deltas.
/// </summary>
public sealed record CardCostChangeStep(
    CardCostChangeKind Kind,
    long Value,
    string? SourceId = null);

public enum CardCostChangeKind
{
    Fixed = 0,
    Modifier = 1,
    Status = 2,
    Combo = 3,
}
