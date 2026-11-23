using System.Collections.Generic;
using RoguelikeCardGame.Core.Data;

namespace RoguelikeCardGame.Core.Battle.Definitions;

/// <summary>
/// 召喚キャラのマスター定義。
/// LifetimeTurns: null = 永続、N = N ターン経過で自動消滅。
/// Deck が空でなければ、固定 Move ではなくカードを使って行動する CPU 味方。
/// 旧 Move 形式は既存データとの互換用に残すが、Deck と同時には設定しない。
/// Phase 10 設計書（10.1.B）第 3-5 章参照。
/// </summary>
public sealed record UnitDefinition(
    string Id,
    string Name,
    string ImageId,
    int Hp,
    string InitialMoveId,
    IReadOnlyList<MoveDefinition> Moves,
    int? LifetimeTurns = null,
    int HeightTier = 5,
    IReadOnlyList<UnitDeckEntry>? Deck = null,
    int InitialEnergy = 1,
    int EnergyPerTurn = 1,
    int DrawPerTurn = 3,
    int HandLimit = 5,
    string? AiProfileId = null,
    string? TraitDescription = null,
    IReadOnlyList<CharacterSkillDefinition>? Skills = null,
    IReadOnlyList<string>? OwnedRelicIds = null,
    IReadOnlyList<string>? Potions = null,
    int Attack = 0,
    int Defense = 0)
    : CombatActorDefinition(Id, Name, ImageId, Hp, HeightTier, InitialMoveId, Moves)
{
    public IReadOnlyList<UnitDeckEntry> EffectiveDeck =>
        Deck ?? System.Array.Empty<UnitDeckEntry>();

    public bool IsCardControlled => EffectiveDeck.Count > 0;

    /// <summary>Optional CPU abilities, using the same data model as player skills.</summary>
    public IReadOnlyList<CharacterSkillDefinition> EffectiveSkills =>
        Skills ?? System.Array.Empty<CharacterSkillDefinition>();

    /// <summary>Relics equipped when the card-controlled participant is created.</summary>
    public IReadOnlyList<string> EffectiveOwnedRelicIds =>
        OwnedRelicIds ?? System.Array.Empty<string>();

    /// <summary>Potions placed in the participant's independent potion slots.</summary>
    public IReadOnlyList<string> EffectivePotions =>
        Potions ?? System.Array.Empty<string>();
}

/// <summary>
/// 召喚 CPU の固定デッキ1項目。同じカードを複数列挙せず Count で枚数を表す。
/// Priority は AI profile の共通比較後に使う Unit 固有の優先補正。
/// </summary>
public sealed record UnitDeckEntry(
    string CardId,
    int Count = 1,
    bool Upgraded = false,
    int Priority = 0);

/// <summary>Built-in CPU policy IDs accepted by unit authoring data.</summary>
public static class UnitAiProfileIds
{
    public const string LowCostCombo = "low_cost_combo";

    public static bool IsKnown(string id) => id == LowCostCombo;
}
