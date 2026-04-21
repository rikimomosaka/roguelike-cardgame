namespace RoguelikeCardGame.Core.Enemy;

/// <summary>
/// 敵の行動パターン 1 ステップ。Phase 5 では <see cref="Id"/> / <see cref="NextMoveId"/> のみが
/// 使用される（表示用名前 / 次 move への遷移）が、Phase 6 の実戦闘で使う数値フィールドも
/// JSON スキーマ上は保持する。
/// </summary>
public sealed record MoveDefinition(
    string Id,
    string Kind,               // "attack", "block", "buff", "debuff", "multi" etc.
    int? DamageMin,
    int? DamageMax,
    int? Hits,
    int? BlockMin,
    int? BlockMax,
    string? Buff,              // "strength", "weak", ... (Phase 6 で参照)
    int? AmountMin,
    int? AmountMax,
    string NextMoveId);
