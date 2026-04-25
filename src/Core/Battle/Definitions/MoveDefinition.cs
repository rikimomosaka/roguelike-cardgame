using System.Collections.Generic;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Battle.Definitions;

/// <summary>
/// 敵 / 召喚キャラの行動 1 ステップ。state-machine 形式の遷移を持つ。
/// Phase 10 設計書（10.1.B）第 3-2 章参照。
/// </summary>
public sealed record MoveDefinition(
    string Id,
    MoveKind Kind,
    IReadOnlyList<CardEffect> Effects,
    string NextMoveId);
