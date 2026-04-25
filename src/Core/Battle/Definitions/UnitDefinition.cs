using System.Collections.Generic;

namespace RoguelikeCardGame.Core.Battle.Definitions;

/// <summary>
/// 召喚キャラのマスター定義。
/// LifetimeTurns: null = 永続、N = N ターン経過で自動消滅。
/// Phase 10 設計書（10.1.B）第 3-5 章参照。
/// </summary>
public sealed record UnitDefinition(
    string Id,
    string Name,
    string ImageId,
    int Hp,
    string InitialMoveId,
    IReadOnlyList<MoveDefinition> Moves,
    int? LifetimeTurns = null)
    : CombatActorDefinition(Id, Name, ImageId, Hp, InitialMoveId, Moves);
