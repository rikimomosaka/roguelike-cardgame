using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Battle;

public enum BattleOutcome { Pending, Victory }

public sealed record BattleState(
    string EncounterId,
    ImmutableArray<EnemyInstance> Enemies,
    BattleOutcome Outcome);

public sealed record EnemyInstance(
    string EnemyDefinitionId,
    int CurrentHp,
    int MaxHp,
    string CurrentMoveId);
