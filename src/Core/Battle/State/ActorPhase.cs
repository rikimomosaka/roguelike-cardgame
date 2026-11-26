namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// キャラの位相。バフ・デバフ（Statuses）とは別の状態で、同時に 1 つだけ持つ。
/// 後から得た位相が前の位相を上書きする。docs/current-phase-spec.md 参照。
/// </summary>
public enum ActorPhase
{
    /// <summary>位相なし（通常状態）。</summary>
    None = 0,
    /// <summary>静。</summary>
    Calm = 1,
    /// <summary>動。</summary>
    Motion = 2,
}
