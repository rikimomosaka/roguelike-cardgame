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
    int EnergyMax,
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
    /// 10.5.M4: ワイルド/スーパーワイルド系統が現在のコンボ連鎖中で既に発動済か。
    /// true のあいだ、後続の wild / superwild キーワードはコンボ継続効果を発揮しない。
    /// コンボ切断 (newCombo = 1) で false にリセット。
    /// </summary>
    bool WildUsedInCurrentCombo = false,
    /// <summary>Phase 10.6.B: cardsDrawnPerTurnBonus modifier 適用済の毎ターンドロー枚数 (battle 開始時 snapshot)。</summary>
    int DrawPerTurn = 5,
    /// <summary>Phase 10.5.M2-Choose: choose 中の保留状態。null=非保留。</summary>
    PendingCardPlay? PendingCardPlay = null);
