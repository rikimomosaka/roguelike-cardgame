using System;
using RoguelikeCardGame.Core.Battle.State;

namespace RoguelikeCardGame.Core.Battle.Engine;

/// <summary>
/// CardEffect.AmountSource の値を runtime state から評価する純関数群。
/// Phase 10.5.D で導入。10.5.B で formatter spec として整備した Variable X
/// (例: 手札の数 = ダメージ量) を engine 側で実際に runtime 値に置換する。
/// 親 spec: docs/superpowers/specs/2026-05-01-phase10-5-design.md §1-3 Q2.
/// </summary>
internal static class AmountSourceEvaluator
{
    /// <summary>
    /// AmountSource を runtime 値に評価する。
    /// 副作用なし、state 不変。未知 source は InvalidOperationException で typo 早期検出。
    /// </summary>
    public static int Evaluate(string source, BattleState state, CombatActor caster)
    {
        return source switch
        {
            "handCount"        => state.Hand.Length,
            "drawPileCount"    => state.DrawPile.Length,
            "discardPileCount" => state.DiscardPile.Length,
            "exhaustPileCount" => state.ExhaustPile.Length,
            "selfHp"           => caster.CurrentHp,
            "selfHpLost"       => caster.MaxHp - caster.CurrentHp,
            "selfBlock"        => GetBlockDisplay(caster),
            "comboCount"       => state.ComboCount,
            "maxComboThisTurn" => state.MaxComboThisTurn,
            "energy"           => state.Energy,
            "powerCardCount"   => state.PowerCards.Length,
            // 能力値参照（筋力・敏捷とは別の基礎値）。筋力・敏捷は従来どおり解決時に加算される。
            "selfAttack"       => caster.AttackPower,
            "selfDefense"      => caster.DefensePower,
            _ => throw new InvalidOperationException(
                $"Unknown AmountSource: '{source}'"),
        };
    }

    /// <summary>倍率（%）を掛けて切り捨てる。null は等倍。int 範囲で飽和する。</summary>
    public static int Scale(int value, int? scalePercent)
    {
        if (scalePercent is not { } pct || pct == 100) return value;
        long scaled = (long)value * pct / 100;
        return (int)Math.Clamp(scaled, int.MinValue, int.MaxValue);
    }

    /// <summary>
    /// 敵の予約では使用時に固定せず、行動実行時の有効値で評価する能力値参照 source。
    /// </summary>
    public static bool IsActorStatSource(string? source) =>
        source is "selfAttack" or "selfDefense";

    /// <summary>
    /// 既存 BlockDisplay 計算経路 (Server BattleStateDtoMapper) と同一の式:
    /// `Block.Display(GetStatus("dexterity"))`。dexterity による遡及 add-count ボーナスを含む。
    /// </summary>
    private static int GetBlockDisplay(CombatActor caster)
        => caster.Block.Display(caster.GetStatus("dexterity"));
}
