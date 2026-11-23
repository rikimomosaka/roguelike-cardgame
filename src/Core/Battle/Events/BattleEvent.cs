using RoguelikeCardGame.Core.Battle.State;

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
    string? Note = null,
    int? BlockedAmount = null,
    int? HitIndex = null,
    DamageType? DamageType = null,
    CombatSource? Source = null,
    DamageCauseChain? CauseChain = null,
    int? ActualHpLoss = null,
    int? Overkill = null,
    bool? HitLanded = null,
    PlayedCardEffectSnapshot? ReenactedCard = null,
    /// <summary>
    /// The authoritative combo count immediately after an actual card play
    /// joins (or restarts) the combo. This is captured before a Finisher can
    /// reset the state back to zero and is null on non-card events.
    /// </summary>
    int? ComboCountAfter = null);
