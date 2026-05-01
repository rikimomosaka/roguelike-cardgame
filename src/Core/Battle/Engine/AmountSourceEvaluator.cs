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
            "energy"           => state.Energy,
            "powerCardCount"   => state.PowerCards.Length,
            _ => throw new InvalidOperationException(
                $"Unknown AmountSource: '{source}'"),
        };
    }

    /// <summary>
    /// 既存 BlockDisplay 計算経路 (Server BattleStateDtoMapper) と同一の式:
    /// `Block.Display(GetStatus("dexterity"))`。dexterity による遡及 add-count ボーナスを含む。
    /// </summary>
    private static int GetBlockDisplay(CombatActor caster)
        => caster.Block.Display(caster.GetStatus("dexterity"));
}
