namespace RoguelikeCardGame.Core.Battle.Definitions;

/// <summary>
/// 敵 / 召喚キャラの move を intent UI 上どのカテゴリで表示するかの分類。
/// 値は battle-v10.html の .is-{attack|defend|buff|debuff|heal|unknown} CSS クラスへ対応。
/// </summary>
public enum MoveKind
{
    Attack  = 0,
    Defend  = 1,
    Buff    = 2,
    Debuff  = 3,
    Heal    = 4,
    Multi   = 5,
    Unknown = 6,
}
