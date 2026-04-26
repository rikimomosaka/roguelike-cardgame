namespace RoguelikeCardGame.Core.Battle.Events;

/// <summary>
/// バトル中に発火されるイベント種別。Phase 10.2.B で 12 種に拡張。
/// 後続 phase で Summon / Exhaust / Upgrade / RelicTrigger / UsePotion 等を追加していく。
/// </summary>
public enum BattleEventKind
{
    BattleStart   = 0,
    TurnStart     = 1,
    PlayCard      = 2,
    AttackFire    = 3,
    DealDamage    = 4,
    GainBlock     = 5,
    ActorDeath    = 6,
    EndTurn       = 7,
    BattleEnd     = 8,
    ApplyStatus   = 9,    // 10.2.B 新規（buff/debuff 付与・重ね掛け）
    RemoveStatus  = 10,   // 10.2.B 新規（countdown で 0 → 削除）
    PoisonTick    = 11,   // 10.2.B 新規（毒ダメージ）
}
