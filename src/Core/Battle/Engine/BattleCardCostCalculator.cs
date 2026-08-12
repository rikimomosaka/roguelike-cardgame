using System;
using RoguelikeCardGame.Core.Battle.State;
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

        return new CardCostBreakdown(
            OriginalCost: original,
            CostBeforeCombo: beforeCombo,
            PayableCost: payable,
            ComboReductionEligible: comboReductionEligible && beforeCombo is not null);
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
            || state.NextCardComboFreePass;
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

    /// <summary>手札表示と PlayCard が共有する、現在支払うコストの完全な投影。</summary>
    public static CardCostBreakdown EvaluateForHand(
        BattleState state,
        BattleCardInstance card,
        CardDefinition definition,
        CombatActor owner) => Evaluate(
            card,
            definition,
            owner,
            isInHand: true,
            comboReductionEligible: IsComboReductionEligible(state, card, definition));

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
    public bool DiffersFromOriginal => OriginalCost != PayableCost;
}
