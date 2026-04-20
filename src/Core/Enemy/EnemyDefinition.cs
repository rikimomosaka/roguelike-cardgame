using System.Collections.Generic;

namespace RoguelikeCardGame.Core.Enemy;

/// <summary>敵のマスター定義。</summary>
public sealed record EnemyDefinition(
    string Id,
    string Name,
    int HpMin,
    int HpMax,
    EnemyPool Pool,
    IReadOnlyList<string> Moveset);
