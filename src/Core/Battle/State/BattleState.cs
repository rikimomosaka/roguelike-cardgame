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
    /// Independently controlled card-resource owners in stable turn order.
    /// Empty/default is a supported legacy solo snapshot; use
    /// <see cref="BattleParticipantAccess"/> to read or update it.
    /// </summary>
    public ImmutableArray<BattleParticipantState> Participants { get; init; } =
        ImmutableArray<BattleParticipantState>.Empty;

    /// <summary>
    /// カード制御の敵ごとのカード資源（山札・手札・捨札・エナジー・コンボ）。
    /// 味方の <see cref="Participants"/> とは分離し、Ready 同期・人間コマンド・
    /// root ミラーには参加しない。敵の計画はターン開始処理内で完結する。
    /// </summary>
    public ImmutableArray<BattleParticipantState> EnemyParticipants { get; init; } =
        ImmutableArray<BattleParticipantState>.Empty;

    /// <summary>
    /// 直近のターン開始時に敵が計画したカード使用の記録（行動再生・開発確認用の観測データ）。
    /// ゲームルールには使わない。次の敵計画開始時に置き換わる。各記録の前後 state は
    /// この欄を空にしたスナップショットなので、入れ子は 1 段に留まる。
    /// </summary>
    public ImmutableArray<Engine.CpuCommittedAction> EnemyPlanningActions { get; init; } =
        ImmutableArray<Engine.CpuCommittedAction>.Empty;

    /// <summary>
    /// Monotonic action-stream revision reserved for authoritative command and
    /// replay coordination. Legacy snapshots start at zero.
    /// </summary>
    public long ActionRevision { get; init; }

    /// <summary>
    /// CPU card plays committed by every participant in the current turn.
    /// Unlike the per-participant diagnostic count, this survives removal of a
    /// dead summon so the battle-wide safety cap cannot be bypassed by cleanup.
    /// </summary>
    public int CpuTotalCardPlaysThisTurn { get; init; }

    /// <summary>Battle owner character definition used to resolve data-driven skills.</summary>
    public string CharacterId { get; init; } = "default";

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

    /// <summary>Per-skill use counts for the current turn. Missing entries mean zero.</summary>
    public ImmutableDictionary<string, int> CharacterSkillUsesThisTurn { get; init; } =
        ImmutableDictionary<string, int>.Empty;

    /// <summary>Per-trait activation counts for the current turn.</summary>
    public ImmutableDictionary<string, int> CharacterTraitUsesThisTurn { get; init; } =
        ImmutableDictionary<string, int>.Empty;

    /// <summary>Per-card-definition play counts for the current turn.</summary>
    public ImmutableDictionary<string, int> CardUsesThisTurn { get; init; } =
        ImmutableDictionary<string, int>.Empty;

    /// <summary>Per-potion-definition use counts for the current turn.</summary>
    public ImmutableDictionary<string, int> PotionUsesThisTurn { get; init; } =
        ImmutableDictionary<string, int>.Empty;

    /// <summary>
    /// Set when Curse reaches zero after a card play. It survives removal of the
    /// visible Curse status and is reset at the next turn boundary.
    /// </summary>
    public bool CardUseLockedThisTurn { get; init; }

    /// <summary>Compatibility mirror of the primary participant's real-card history.</summary>
    public ImmutableArray<PlayedCardEffectSnapshot> PlayedCardHistoryThisTurn { get; init; } =
        ImmutableArray<PlayedCardEffectSnapshot>.Empty;

    /// <summary>
    /// Compatibility mirror of effect snapshots eligible for Reprise. Unlike
    /// PlayedCardHistoryThisTurn, this history persists across turn boundaries.
    /// </summary>
    public ImmutableArray<PlayedCardEffectSnapshot> RepriseEligibleHistory { get; init; } =
        ImmutableArray<PlayedCardEffectSnapshot>.Empty;

    /// <summary>Compatibility mirror of the primary participant's active combo history.</summary>
    public ImmutableArray<PlayedCardEffectSnapshot> CurrentComboCardHistory { get; init; } =
        ImmutableArray<PlayedCardEffectSnapshot>.Empty;

    /// <summary>Compatibility mirror of the primary participant's latest Finisher record.</summary>
    public ImmutableArray<PlayedCardEffectSnapshot> LastFinisherRecord { get; init; } =
        ImmutableArray<PlayedCardEffectSnapshot>.Empty;

    /// <summary>Compatibility mirror of armed primary-participant Contingencies.</summary>
    public ImmutableArray<ArmedContingency> ArmedContingencies { get; init; } =
        ImmutableArray<ArmedContingency>.Empty;

    public long NextCardPlaySequence { get; init; }

    /// <summary>Next deterministic sequence for a completed effect resolution.</summary>
    public long NextEffectResolutionSequence { get; init; }

    public long NextContingencyOrdinal { get; init; }

    /// <summary>
    /// Battle-wide deterministic sequence used for cards created by effects.
    /// It is global because card instance ids share one command/replay
    /// namespace across every participant in the battle.
    /// </summary>
    public long NextGeneratedCardOrdinal { get; init; }

    public bool ComboCarryoverPending { get; init; }

    /// <summary>
    /// Actor ids for which Afterglow actually suppressed at least one regular
    /// side-status countdown during the current shared turn boundary. The
    /// marker survives queued-resolution pauses and is cleared by the formal
    /// turn-end status processor.
    /// </summary>
    public ImmutableHashSet<string> AfterglowProtectedActorInstanceIds { get; init; } =
        ImmutableHashSet<string>.Empty;

    /// <summary>
    /// Non-null only while queued turn resolution is waiting for a human
    /// choice raised by Contingency or Afterimage.
    /// </summary>
    public PendingTurnResolution? PendingTurnResolution { get; init; }
}
