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
/// <param name="UpgradedKeywords">強化時のキーワード能力。null/省略 = 強化時はキーワードなし</param>
/// <param name="Description">手書き description override (任意)。null/空文字なら formatter で自動生成。</param>
/// <param name="UpgradedDescription">強化版 description override (任意)。null/空文字なら formatter で自動生成。</param>
/// <param name="UsesPerTurn">通常版カード本体の1ターン使用上限。0は制限なし。</param>
/// <param name="UpgradedUsesPerTurn">強化版カード本体の独立した1ターン使用上限。null/0は制限なし。</param>
/// <param name="Abilities">通常版の型付きカード能力。null/省略 = なし。</param>
/// <param name="UpgradedAbilities">強化版の型付きカード能力。null/省略 = 強化時は能力なし。</param>
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
    IReadOnlyList<string>? UpgradedKeywords = null,
    string? Description = null,
    string? UpgradedDescription = null,
    int ComboGaugeCost = 0,
    int UsesPerTurn = 0,
    int? UpgradedUsesPerTurn = null,
    RoguelikeCardGame.Core.Data.CardPoolClass? PoolClass = null,
    IReadOnlyList<RoguelikeCardGame.Core.Data.CardPoolKind>? PoolKinds = null,
    IReadOnlyList<string>? EligibleCharacterIds = null,
    IReadOnlyList<CardAbilityDefinition>? Abilities = null,
    IReadOnlyList<CardAbilityDefinition>? UpgradedAbilities = null)
{
    /// <summary>通常プール外のカードでは空。JSON 欠落時も空として後方互換に扱う。</summary>
    /// <summary>
    /// 敵が使うカード（ID が enemy_ で始まる）。レア度はコモン（基本行動）・レア（汎用の特殊行動）・
    /// エピック（固有行動）の表示に使うだけで、報酬・お店・図鑑のカード収集には含めない。
    /// </summary>
    public bool IsEnemyCard => Id.StartsWith("enemy_", System.StringComparison.Ordinal);

    public IReadOnlyList<RoguelikeCardGame.Core.Data.CardPoolKind> EffectivePoolKinds =>
        PoolKinds ?? System.Array.Empty<RoguelikeCardGame.Core.Data.CardPoolKind>();

    /// <summary>空なら所属クラスの全キャラクターが取得可能。</summary>
    public IReadOnlyList<string> EffectiveEligibleCharacterIds =>
        EligibleCharacterIds ?? System.Array.Empty<string>();

    /// <summary>Shared resource view used by cards and character skills.</summary>
    public RoguelikeCardGame.Core.Data.ResourceCost ResourceCost =>
        new(Cost ?? 0, ComboGaugeCost);

    /// <summary>
    /// UpgradedCost / UpgradedEffects / UpgradedKeywords / UpgradedUsesPerTurn の
    /// いずれかが指定されているとき強化可能。
    /// すべて null/省略のカードは強化対象外。
    /// </summary>
    public bool IsUpgradable => UpgradedCost is not null
        || UpgradedEffects is not null
        || UpgradedKeywords is not null
        || UpgradedUsesPerTurn is not null
        || UpgradedAbilities is not null;

    /// <summary>
    /// 強化状態に応じた有効キーワードを返す。
    /// 通常版と強化版は独立しており、強化版は UpgradedKeywords だけを参照する。
    /// </summary>
    public IReadOnlyList<string>? EffectiveKeywords(bool upgraded) =>
        upgraded ? UpgradedKeywords : Keywords;

    /// <summary>通常版・強化版それぞれのカード本体に設定されたターン使用上限。</summary>
    public int EffectiveUsesPerTurn(bool upgraded) =>
        upgraded ? UpgradedUsesPerTurn ?? 0 : UsesPerTurn;

    /// <summary>
    /// 通常版と強化版それぞれの型付き能力。Legacy keywords と同じく独立指定とし、
    /// upgraded=true のとき通常版から暗黙継承しない。
    /// </summary>
    public IReadOnlyList<CardAbilityDefinition> EffectiveAbilities(bool upgraded) =>
        (upgraded ? UpgradedAbilities : Abilities) ?? System.Array.Empty<CardAbilityDefinition>();
}
