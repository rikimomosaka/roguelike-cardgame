using System.Collections.Generic;

namespace RoguelikeCardGame.Core.Battle.Definitions;

/// <summary>
/// 戦闘に参加するキャラクターの静的定義（敵・召喚キャラの共通基底）。
/// HP は単一値。乱数化は将来拡張ポイント。
/// Phase 10 設計書（10.1.B）第 3-3 章参照。
/// </summary>
public abstract record CombatActorDefinition(
    string Id,
    string Name,
    string ImageId,
    int Hp,
    string InitialMoveId,
    IReadOnlyList<MoveDefinition> Moves);
