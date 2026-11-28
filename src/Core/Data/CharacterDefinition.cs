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
    int Defense = 0,
    IReadOnlyList<string>? OpeningActions = null)
{
    /// <summary>
    /// 初期行動（戦闘開始時、敵の初期行動の後・プレイヤーの操作の前に使うカード ID。記載順）。
    /// 今はどのキャラにも設定していないが、設定すれば動く。
    /// </summary>
    public IReadOnlyList<string> EffectiveOpeningActions =>
        OpeningActions ?? System.Array.Empty<string>();

    public IReadOnlyList<CharacterSkillDefinition> EffectiveSkills =>
        Skills ?? System.Array.Empty<CharacterSkillDefinition>();
}
