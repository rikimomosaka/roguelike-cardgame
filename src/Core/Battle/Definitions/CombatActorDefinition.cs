using System.Collections.Generic;

namespace RoguelikeCardGame.Core.Battle.Definitions;

/// <summary>
/// 戦闘に参加するキャラクターの静的定義（敵・召喚キャラの共通基底）。
/// HP は単一値。乱数化は将来拡張ポイント。
/// HeightTier は立ち絵の高さ段階 (1〜10、5 が標準)。
/// Phase 10 設計書（10.1.B）第 3-3 章参照。
/// </summary>
public abstract record CombatActorDefinition(
    string Id,
    string Name,
    string ImageId,
    int Hp,
    int HeightTier,
    string InitialMoveId,
    IReadOnlyList<MoveDefinition> Moves);
