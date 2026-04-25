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

/// <summary>placeholder 用の旧 BattleOutcome。Phase 10.5 で削除し、新 <see cref="State.BattleOutcome"/> に統合。</summary>
public enum BattleOutcome { Pending, Victory }
