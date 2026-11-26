using System;
using System.Collections.Generic;

namespace RoguelikeCardGame.Core.Cards;

/// <summary>One status and its signed stack change inside a buff/debuff effect.</summary>
public sealed record StatusChange(string Name, int Amount);

/// <summary>
/// カード／敵 Move／召喚 Move／レリック／ポーション 共通の効果プリミティブ。
/// Phase 10 設計書 (2026-04-25-phase10-battle-system-design.md) 第 2-1 章参照。
/// </summary>
/// <param name="Action">"attack"|"block"|"buff"|"debuff"|"summon"|"heal"|"draw"|"discard"|"upgrade"|"exhaustCard"|"exhaustSelf"|"retainSelf"|"gainEnergy"|"gainComboGauge"|"setPhase"|"invertPhase"|"augmentCards" など</param>
/// <param name="Scope">対象スコープ</param>
/// <param name="Side">対象側（行動主体からの相対視点）。Self では null</param>
/// <param name="Amount">効果量</param>
/// <param name="Name">buff/debuff の種類名 ("strength"|"vulnerable" 等)</param>
/// <param name="UnitId">summon 用：召喚キャラ ID</param>
/// <param name="ComboMin">コンボ N 以上で適用（カードのみ意味あり）</param>
/// <param name="Pile">"hand"|"discard"|"draw" (exhaustCard / upgrade / discard 用)</param>
/// <param name="BattleOnly">true なら戦闘外発動時にスキップ</param>
/// <param name="CardRefId">10.5.B reserved: addCard 等で参照する既存カード id。engine 動作は 10.5.F で実装。</param>
/// <param name="Select">10.5.B reserved: discard 等の選択方式。"random" | "choose" | "all"。engine 動作は 10.5.F で実装。</param>
/// <param name="AmountSource">10.5.B reserved: Variable X 等の amount ソース。"handCount" | "drawPileCount" 等。engine 動作は 10.5.D で実装。</param>
/// <param name="Trigger">10.5.B reserved: power カードの発火タイミング。"OnTurnStart" | "OnPlayCard" 等。engine 動作は 10.5.E で実装。</param>
/// <param name="CostDuration">modifyCardCost 専用。"untilPlayed" | "untilTurnEnd"。</param>
/// <param name="CardCount">modifyCardCost で random/choose するカード枚数。Select=all では未使用。</param>
/// <param name="MaxComboMin">乗勢X。効果実行時の自身のターン内最大コンボがX以上の場合だけ適用。</param>
/// <param name="AmountScalePercent">AmountSource の評価値に掛ける倍率（%）。例: 150 = 1.5 倍（切り捨て）。null は 100%。</param>
public sealed record CardEffect(
    string Action,
    EffectScope Scope,
    EffectSide? Side,
    int Amount,
    string? Name = null,
    string? UnitId = null,
    int? ComboMin = null,
    string? Pile = null,
    bool BattleOnly = false,
    string? CardRefId = null,
    string? Select = null,
    string? AmountSource = null,
    string? Trigger = null,
    int? ChancePercent = null,
    string? CostDuration = null,
    int? CardCount = null,
    IReadOnlyList<StatusChange>? StatusChanges = null,
    int? MaxComboMin = null,
    int? AmountScalePercent = null,
    string? CardTypeFilter = null)
{
    private static readonly IReadOnlyList<StatusChange> EmptyStatusChanges = Array.Empty<StatusChange>();

    /// <summary>
    /// Returns the grouped status changes, or adapts the legacy name/amount pair.
    /// New content may group multiple statuses of the same kind and target in one effect,
    /// while old JSON remains valid without migration.
    /// </summary>
    public IReadOnlyList<StatusChange> EffectiveStatusChanges()
    {
        if (StatusChanges is { Count: > 0 }) return StatusChanges;
        return string.IsNullOrWhiteSpace(Name)
            ? EmptyStatusChanges
            : new[] { new StatusChange(Name, Amount) };
    }

    /// <summary>
    /// JSON ロード時の safety net 正規化。
    /// - Scope=Self なら Side を破棄（null に）
    /// - Action=="attack" なら Side を Enemy に強制
    /// </summary>
    public CardEffect Normalize()
    {
        var side = Side;
        if (Scope == EffectScope.Self) side = null;
        if (Action == "attack") side = EffectSide.Enemy;
        return this with { Side = side };
    }
}
