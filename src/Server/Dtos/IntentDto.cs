namespace RoguelikeCardGame.Server.Dtos;

/// <summary>
/// 次に actor が行う予定行動のサマリ表示。Client が頭上にチップを出す用途。
/// Server 側で actor.CurrentMoveId + DataCatalog (敵/召喚) または actor pool
/// (hero) から計算する。Phase 10.3-MVP UX 拡張。
///
/// 攻撃量は scope ごとに個別保持し、通常/ランダム/全体で色分け表示できる。
/// 攻撃以外は block / buff / debuff / heal の有無/量を別フィールドで持つ。
/// </summary>
/// <param name="AttackSingle">通常攻撃 (single) の予定ダメージ。0/null = なし。</param>
/// <param name="AttackRandom">ランダム攻撃 (random) の予定ダメージ。</param>
/// <param name="AttackAll">全体攻撃 (all) の予定ダメージ。</param>
/// <param name="AttackHits">攻撃回数 (effects 中 attack の数)。</param>
/// <param name="Block">block 予定値。</param>
/// <param name="HasBuff">buff 効果の有無。</param>
/// <param name="HasDebuff">debuff 効果の有無。</param>
/// <param name="HasHeal">heal 効果の有無。</param>
public sealed record IntentDto(
    int? AttackSingle,
    int? AttackRandom,
    int? AttackAll,
    int? AttackHits,
    int? Block,
    bool HasBuff,
    bool HasDebuff,
    bool HasHeal);
