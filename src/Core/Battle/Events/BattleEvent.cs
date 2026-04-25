namespace RoguelikeCardGame.Core.Battle.Events;

/// <summary>
/// バトル中の 1 イベント。`BattleEngine` の各公開メソッドが
/// `IReadOnlyList&lt;BattleEvent&gt;` として時系列順に返す。
/// Phase 10.3 で `BattleEventDto` に変換され Client に push される。
/// 親 spec §9-7 参照。
/// </summary>
public sealed record BattleEvent(
    BattleEventKind Kind,
    int Order,
    string? CasterInstanceId = null,
    string? TargetInstanceId = null,
    int? Amount = null,
    string? CardId = null,
    string? Note = null);
