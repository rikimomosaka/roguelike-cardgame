using System.Collections.Generic;

namespace RoguelikeCardGame.Core.Battle.Definitions;

/// <summary>
/// 敵のマスター定義。
/// Deck が空でなければカードで行動する CPU 敵（docs/proposed-enemy-card-ai-and-character-stats.md）。
/// Deck が無い敵は移行完了まで旧 state-machine 形式の Move で行動する。
/// Phase 10 設計書（10.1.B）第 3-4 章参照。
/// </summary>
public sealed record EnemyDefinition(
    string Id,
    string Name,
    string ImageId,
    int Hp,
    EnemyPool Pool,
    string InitialMoveId,
    IReadOnlyList<MoveDefinition> Moves,
    int HeightTier = 5,
    IReadOnlyList<UnitDeckEntry>? Deck = null,
    int InitialEnergy = 0,
    int EnergyPerTurn = 1,
    int DrawPerTurn = 3,
    int HandLimit = 5,
    string? AiProfileId = null,
    int Attack = 0,
    int Defense = 0,
    IReadOnlyList<string>? OpeningActions = null)
    : CombatActorDefinition(Id, Name, ImageId, Hp, HeightTier, InitialMoveId, Moves)
{
    /// <summary>通常行動デッキ（共通カード＋固有カード）。先制行動はここに含めない。</summary>
    public IReadOnlyList<UnitDeckEntry> EffectiveDeck =>
        Deck ?? System.Array.Empty<UnitDeckEntry>();

    public bool IsCardControlled => EffectiveDeck.Count > 0;

    /// <summary>
    /// 先制行動プール（カード ID、記載順に実行）。通常デッキとは独立し、
    /// 戦闘開始時に最初の手札を配った直後、他の全処理の前に即時解決する。
    /// </summary>
    public IReadOnlyList<string> EffectiveOpeningActions =>
        OpeningActions ?? System.Array.Empty<string>();
}
