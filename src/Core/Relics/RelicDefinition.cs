using System.Collections.Generic;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Relics;

/// <summary>レリックのマスター定義。</summary>
public sealed record RelicDefinition(
    string Id,
    string Name,
    CardRarity Rarity,
    RelicTrigger Trigger,
    IReadOnlyList<CardEffect> Effects);
