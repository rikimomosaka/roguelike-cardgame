using System.Collections.Generic;

namespace RoguelikeCardGame.Core.Data;

/// <summary>プレイアブルキャラのマスター定義。Phase 5 では "default" のみ使用。</summary>
public sealed record CharacterDefinition(
    string Id,
    string Name,
    int MaxHp,
    int StartingGold,
    int PotionSlotCount,
    IReadOnlyList<string> Deck);
