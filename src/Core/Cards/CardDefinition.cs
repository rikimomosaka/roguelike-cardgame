using System.Collections.Generic;

namespace RoguelikeCardGame.Core.Cards;

/// <summary>カードのマスター定義。JSON から読み込まれる不変データ。</summary>
/// <param name="Id">一意の英数字 ID（内部参照用）。</param>
/// <param name="Name">カード名。</param>
/// <param name="DisplayName">表示名。プロモ・スキン違いで差し替える任意項目。null なら Name を表示。</param>
/// <param name="Rarity">レアリティ。</param>
/// <param name="CardType">カード種別。</param>
/// <param name="Cost">プレイコスト。null はプレイ不可（条件付き起動等を表現）。</param>
/// <param name="Effects">効果プリミティブ配列。</param>
/// <param name="UpgradedEffects">強化時の効果配列。null なら強化不可。</param>
public sealed record CardDefinition(
    string Id,
    string Name,
    string? DisplayName,
    CardRarity Rarity,
    CardType CardType,
    int? Cost,
    IReadOnlyList<CardEffect> Effects,
    IReadOnlyList<CardEffect>? UpgradedEffects);
