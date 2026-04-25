using System.Collections.Generic;

namespace RoguelikeCardGame.Core.Cards;

/// <summary>カードのマスター定義。Phase 10 設計書 第 2-3 章参照。</summary>
/// <param name="Id">一意の英数字 ID</param>
/// <param name="Name">カード名</param>
/// <param name="DisplayName">表示名（省略可、null なら Name を表示）</param>
/// <param name="Rarity">レアリティ</param>
/// <param name="CardType">カード種別</param>
/// <param name="Cost">プレイコスト。null はプレイ不可</param>
/// <param name="UpgradedCost">強化後のプレイコスト。null/省略 = Cost と同じ</param>
/// <param name="Effects">効果プリミティブ配列</param>
/// <param name="UpgradedEffects">強化時の効果配列。null/省略 = Effects と同じ</param>
/// <param name="Keywords">キーワード能力（"wild"|"superwild" 等）。null/省略 = なし</param>
public sealed record CardDefinition(
    string Id,
    string Name,
    string? DisplayName,
    CardRarity Rarity,
    CardType CardType,
    int? Cost,
    int? UpgradedCost,
    IReadOnlyList<CardEffect> Effects,
    IReadOnlyList<CardEffect>? UpgradedEffects,
    IReadOnlyList<string>? Keywords)
{
    /// <summary>
    /// UpgradedCost か UpgradedEffects のどちらかが指定されているとき強化可能。
    /// 両方とも null/省略のカードは強化対象外。
    /// </summary>
    public bool IsUpgradable => UpgradedCost is not null || UpgradedEffects is not null;
}
