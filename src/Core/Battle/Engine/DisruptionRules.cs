using System;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Random;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// 攪乱（デバフ）の行動失敗。攪乱を持つキャラの行動（カード 1 枚の使用）は、
/// 攪乱の数 × 10%（最大 50%）の確率で失敗し、そのカードの効果が出ない。
/// 攻撃のランダム化は <see cref="AttackScopeRules"/>。
/// </summary>
public static class DisruptionRules
{
    /// <summary>失敗率の上限（%）。</summary>
    public const int MaxFailurePercent = 50;

    /// <summary>攪乱 1 つあたりの失敗率（%）。</summary>
    public const int FailurePercentPerStack = 10;

    /// <summary>攪乱の数から失敗率（%）を出す。</summary>
    public static int FailurePercent(int disruption)
        => disruption <= 0
            ? 0
            : (int)Math.Min(MaxFailurePercent, (long)disruption * FailurePercentPerStack);

    /// <summary>
    /// 行動 1 回の失敗判定。攪乱が無いときは乱数を消費しない（既存の乱数列を変えないため）。
    /// </summary>
    public static bool RollActionFailure(CombatActor actor, IRng rng)
    {
        int percent = FailurePercent(actor.GetStatus(BattleStatusIds.Disruption));
        return percent > 0 && rng.NextInt(0, 100) < percent;
    }
}
