using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Battle;

/// <summary>
/// Phase 5 placeholder バトルの state。Phase 10.5 で削除予定。
/// 新本格バトルは <see cref="RoguelikeCardGame.Core.Battle.State.BattleState"/>。
/// </summary>
public sealed record BattlePlaceholderState(
    string EncounterId,
    ImmutableArray<PlaceholderEnemyInstance> Enemies,
    BattleOutcome Outcome);

public sealed record PlaceholderEnemyInstance(
    string EnemyDefinitionId,
    int CurrentHp,
    int MaxHp,
    string CurrentMoveId);

/// <summary>
/// placeholder 用の旧 BattleOutcome（Pending, Victory）。Phase 10.5 で削除し、
/// 新 <see cref="State.BattleOutcome"/> (Pending, Victory, Defeat) に統合する。
///
/// **CS0104 ambiguity risk**: 同じ assembly 内に <see cref="State.BattleOutcome"/> が存在し、
/// メンバ名（Pending / Victory）も重複する。同一 .cs ファイルで両 namespace を using すると
/// `BattleOutcome` 参照がコンパイルエラーになる。両方必要な場合は次のいずれかで回避:
///   using BattleOutcome = RoguelikeCardGame.Core.Battle.BattleOutcome;
///   using NewBattleOutcome = RoguelikeCardGame.Core.Battle.State.BattleOutcome;
/// </summary>
public enum BattleOutcome { Pending, Victory }
