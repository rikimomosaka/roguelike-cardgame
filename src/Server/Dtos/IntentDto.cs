namespace RoguelikeCardGame.Server.Dtos;

/// <summary>
/// 次に actor が行う予定行動のサマリ表示。Client が頭上にチップを出す用途。
/// Server 側で actor.CurrentMoveId + DataCatalog (敵/召喚) または actor pool
/// (hero) から計算する。Phase 10.3-MVP UX 拡張。
///
/// 攻撃量は scope ごとに個別保持し、通常/ランダム/全体で色分け表示できる。
/// 攻撃以外は block / buff / debuff / heal の有無/量を別フィールドで持つ。
/// </summary>
/// <param name="AttackSingle">single の補正後合計。null = なし、0 = 0ダメージの実ヒットあり。</param>
/// <param name="AttackRandom">random の補正後合計。null = なし、0 = 実ヒットあり。</param>
/// <param name="AttackAll">all の補正後合計。null = なし、0 = 実ヒットあり。</param>
/// <param name="AttackHits">全 scope を合計した攻撃回数。</param>
/// <param name="AttackSingleHits">single の攻撃回数。</param>
/// <param name="AttackRandomHits">random の攻撃回数。</param>
/// <param name="AttackAllHits">all の攻撃回数。</param>
/// <param name="Block">block 予定値。</param>
/// <param name="HasBuff">buff 効果の有無。</param>
/// <param name="HasDebuff">debuff 効果の有無。</param>
/// <param name="HasHeal">heal 効果の有無。</param>
public sealed record IntentDto(
    int? AttackSingle,
    int? AttackRandom,
    int? AttackAll,
    int? AttackHits,
    int? AttackSingleHits,
    int? AttackRandomHits,
    int? AttackAllHits,
    int? Block,
    bool HasBuff,
    bool HasDebuff,
    bool HasHeal);
