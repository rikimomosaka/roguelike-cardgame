using System.Collections.Generic;
using RoguelikeCardGame.Core.Enemy;

namespace RoguelikeCardGame.Core.Data;

/// <summary>
/// 1 回の戦闘で同時に出現する敵 ID の組。Act / Tier（= <see cref="EnemyPool"/>）とひもづく。
/// </summary>
public sealed record EncounterDefinition(
    string Id,
    EnemyPool Pool,
    IReadOnlyList<string> EnemyIds);
