namespace RoguelikeCardGame.Core.Battle.Events;

/// <summary>
/// バトル中に発生するイベント種別。
/// 10.2.D で 7 値追加（Heal/Draw/Discard/Upgrade/Exhaust/GainEnergy/Summon）。
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
    ApplyStatus   = 9,
    RemoveStatus  = 10,
    PoisonTick    = 11,
    Heal          = 12,    // 10.2.D
    Draw          = 13,    // 10.2.D
    Discard       = 14,    // 10.2.D
    Upgrade       = 15,    // 10.2.D
    Exhaust       = 16,    // 10.2.D（exhaustCard / exhaustSelf 共通）
    GainEnergy    = 17,    // 10.2.D
    Summon        = 18,    // 10.2.D
    UsePotion     = 19,    // 10.2.E
}
