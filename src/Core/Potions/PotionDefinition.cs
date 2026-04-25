using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Potions;

/// <summary>ポーションのマスター定義。</summary>
public sealed record PotionDefinition(
    string Id,
    string Name,
    CardRarity Rarity,
    IReadOnlyList<CardEffect> Effects)
{
    /// <summary>
    /// 戦闘外で使用可能か。effects のいずれかが BattleOnly=false なら true。
    /// 全 effect が BattleOnly=true なら false（マップ画面でグレーアウト）。
    /// Phase 10 設計書（10.1.C）第 3-3 章参照。
    /// </summary>
    public bool IsUsableOutsideBattle => Effects.Any(e => !e.BattleOnly);
}
