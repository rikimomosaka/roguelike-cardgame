using System.Collections.Generic;

namespace RoguelikeCardGame.Core.Enemy;

/// <summary>敵のマスター定義。state-machine 形式の行動セットを持つ。</summary>
public sealed record EnemyDefinition(
    string Id,
    string Name,
    string ImageId,
    int HpMin,
    int HpMax,
    EnemyPool Pool,
    string InitialMoveId,
    IReadOnlyList<MoveDefinition> Moves);
