namespace RoguelikeCardGame.Core.Battle.Events;

/// <summary>
/// バトル中に発火されるイベント種別。Phase 10.2.A の最小セット 9 種。
/// 後続 phase で ApplyStatus / Summon / Exhaust / Upgrade /
/// RelicTrigger / UsePotion 等を追加していく。
/// </summary>
public enum BattleEventKind
{
    BattleStart = 0,
    TurnStart   = 1,
    PlayCard    = 2,
    AttackFire  = 3,
    DealDamage  = 4,
    GainBlock   = 5,
    ActorDeath  = 6,
    EndTurn     = 7,
    BattleEnd   = 8,
}
