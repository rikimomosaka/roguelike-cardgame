using System.Collections.Generic;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Relics;

/// <summary>レリックのマスター定義。</summary>
/// <param name="Implemented">
/// false の場合、エンジンは effects を一切処理しない（戦闘外・戦闘内とも no-op）。
/// プレイヤー所持・図鑑掲載は通常通り。description には [未実装] プレフィックスを付ける。
/// Phase 10 設計書（10.1.C）第 3-1 章参照。
/// </param>
public sealed record RelicDefinition(
    string Id,
    string Name,
    CardRarity Rarity,
    RelicTrigger Trigger,
    IReadOnlyList<CardEffect> Effects,
    string Description = "",
    bool Implemented = true);
