using System.Collections.Generic;

namespace RoguelikeCardGame.Core.Data;

/// <summary>プレイアブルキャラのマスター定義。Phase 5 では "default" のみ使用。</summary>
public sealed record CharacterDefinition(
    string Id,
    string Name,
    int MaxHp,
    int StartingGold,
    int PotionSlotCount,
    IReadOnlyList<string> Deck,
    int HeightTier = 5,
    IReadOnlyList<CharacterSkillDefinition>? Skills = null,
    string? Description = null,
    string? TraitDescription = null,
    CharacterClass CharacterClass = CharacterClass.Striker,
    int EnergyPerTurn = 3,
    int DrawPerTurn = 5,
    CharacterTraitDefinition? Trait = null,
    int Attack = 0,
    int Defense = 0)
{
    public IReadOnlyList<CharacterSkillDefinition> EffectiveSkills =>
        Skills ?? System.Array.Empty<CharacterSkillDefinition>();
}
