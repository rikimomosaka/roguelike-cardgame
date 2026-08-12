namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// カード個体に付与されたコスト増減が失効するタイミング。
/// どちらもカードをプレイした時点で失われる。
/// </summary>
public enum CardCostModifierDuration
{
    /// <summary>ターンや領域をまたいで残り、カードをプレイした時だけ失われる。</summary>
    UntilPlayed = 0,

    /// <summary>カードをプレイするか、そのターンが終了した時に失われる。</summary>
    UntilTurnEnd = 1,
}

/// <summary>
/// バトル中のカード個体に積まれる加算式コスト変更。
/// Amount は減少なら負、増加なら正。SourceId は表示・監査用で計算には影響しない。
/// </summary>
public sealed record CardCostModifier(
    long Amount,
    CardCostModifierDuration Duration,
    string? SourceId = null);
