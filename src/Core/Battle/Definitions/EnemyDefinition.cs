using System.Collections.Generic;

namespace RoguelikeCardGame.Core.Battle.Definitions;

/// <summary>
/// 敵のマスター定義。state-machine 形式の行動セットを持つ。
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
    int HeightTier = 5)
    : CombatActorDefinition(Id, Name, ImageId, Hp, HeightTier, InitialMoveId, Moves);
