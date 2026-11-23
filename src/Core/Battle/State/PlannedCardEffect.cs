using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Cards;

namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// 敵がカード使用時に予約した戦場効果（攻撃・ブロック・バフ・デバフ等）。
/// 予約時には適用せず、敵の行動実行時（プレイヤー側の行動終了後）に
/// 非攻撃効果 → 攻撃効果の順で適用する。
/// Effect は使用時に AmountSource 等を確定済みの値、Source は発生源カード情報。
/// docs/proposed-enemy-card-ai-and-character-stats.md §2-2a 参照。
/// </summary>
public sealed record PlannedCardEffect(
    CardEffect Effect,
    CombatSource Source);
