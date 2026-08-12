using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// バトル全体の不変状態。
/// 親 spec §3-1 参照。
/// 10.2.D で SummonHeld / PowerCards を追加。10.2.E で OwnedRelicIds / Potions を追加。
/// </summary>
public sealed record BattleState(
    int Turn,
    BattlePhase Phase,
    BattleOutcome Outcome,
    ImmutableArray<CombatActor> Allies,
    ImmutableArray<CombatActor> Enemies,
    int? TargetAllyIndex,
    int? TargetEnemyIndex,
    int Energy,
    int EnergyMax,                              // 互換名。現在は「ターン開始時の追加量」であり保有上限ではない
    ImmutableArray<BattleCardInstance> DrawPile,
    ImmutableArray<BattleCardInstance> Hand,
    ImmutableArray<BattleCardInstance> DiscardPile,
    ImmutableArray<BattleCardInstance> ExhaustPile,
    ImmutableArray<BattleCardInstance> SummonHeld,    // 10.2.D
    ImmutableArray<BattleCardInstance> PowerCards,    // 10.2.D
    int ComboCount,
    int? LastPlayedOrigCost,
    bool NextCardComboFreePass,
    ImmutableArray<string> OwnedRelicIds,         // ← 10.2.E 追加
    ImmutableArray<string> Potions,               // ← 10.2.E 追加
    string EncounterId,
    /// <summary>
    /// 現在のコンボ中で Wild が既に発動済みか。
    /// true のあいだ、後続の Wild だけを無効化する。SuperWild は常に有効。
    /// コンボ切断またはターン終了で false にリセット。
    /// </summary>
    bool WildUsedInCurrentCombo = false,
    /// <summary>Phase 10.6.B: cardsDrawnPerTurnBonus modifier 適用済の毎ターンドロー枚数 (battle 開始時 snapshot)。</summary>
    int DrawPerTurn = 5,
    /// <summary>Phase 10.5.M2-Choose: choose 中の保留状態。null=非保留。</summary>
    PendingCardPlay? PendingCardPlay = null)
{
    /// <summary>
    /// このターン中に到達した最大コンボ数。
    /// テスト用など、進行途中の状態を直接構築した場合も不整合にならないよう
    /// 初期値は現在の ComboCount とする。
    /// </summary>
    public int MaxComboThisTurn { get; init; } = ComboCount;

    /// <summary>ターンを跨いで保持するキャラクタースキル用コンボゲージ（0..10）。</summary>
    public int ComboGauge { get; init; }

    /// <summary>このターン中に使用済みの、ターン1回制限を持つキャラクタースキルID。</summary>
    public ImmutableHashSet<string> UsedCharacterSkillIdsThisTurn { get; init; } =
        ImmutableHashSet<string>.Empty;
}
