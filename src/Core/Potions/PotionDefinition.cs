using System.Collections.Generic;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Potions;

/// <summary>ポーションのマスター定義。</summary>
public sealed record PotionDefinition(
    string Id,
    string Name,
    CardRarity Rarity,
    bool UsableInBattle,
    bool UsableOutOfBattle,
    IReadOnlyList<CardEffect> Effects);
