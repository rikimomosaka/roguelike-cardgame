namespace RoguelikeCardGame.Server.Dtos;

/// <summary>
/// 次に actor が行う予定行動のサマリ表示。Client が頭上にチップを出す用途。
/// Server 側で actor.CurrentMoveId + DataCatalog から計算する。
/// Phase 10.3-MVP MVP 拡張 (battle UX 補強)。
/// </summary>
/// <param name="Kind">"attack" | "defend" | "buff" | "debuff" | "heal" | "multi" | "unknown"</param>
/// <param name="Amount">attack の合計ダメージ / defend の block 量。kind によっては null。</param>
/// <param name="Hits">attack の場合の攻撃回数 (複数命中で表示するため)。</param>
public sealed record IntentDto(
    string Kind,
    int? Amount,
    int? Hits);
