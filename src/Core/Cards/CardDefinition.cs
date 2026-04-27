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
/// <param name="UpgradedKeywords">強化時のキーワード能力。null/省略 = Keywords を継承</param>
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
    IReadOnlyList<string>? Keywords,
    IReadOnlyList<string>? UpgradedKeywords = null)
{
    /// <summary>
    /// UpgradedCost / UpgradedEffects / UpgradedKeywords のいずれかが指定されているとき強化可能。
    /// すべて null/省略のカードは強化対象外。
    /// </summary>
    public bool IsUpgradable => UpgradedCost is not null
        || UpgradedEffects is not null
        || UpgradedKeywords is not null;

    /// <summary>
    /// 強化状態に応じた有効キーワードを返す。
    /// upgraded=true かつ UpgradedKeywords が指定されているならそれ、それ以外は Keywords を継承。
    /// </summary>
    public IReadOnlyList<string>? EffectiveKeywords(bool upgraded) =>
        upgraded && UpgradedKeywords is not null ? UpgradedKeywords : Keywords;
}
