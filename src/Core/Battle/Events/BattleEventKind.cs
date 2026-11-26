namespace RoguelikeCardGame.Core.Battle.Events;

/// <summary>
/// バトル中に発生するイベント種別。
/// 10.2.D で 7 値追加（Heal/Draw/Discard/Upgrade/Exhaust/GainEnergy/Summon）。
/// 10.5.F で 3 値追加（AddCard/RecoverFromDiscard/GainMaxEnergy）。
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

    // Phase 10.5.F: engine 新 actions
    /// <summary>カードを effect 経由で zone (hand/draw/discard/exhaust) に追加した。</summary>
    AddCard            = 20,
    /// <summary>カードを discard pile から hand or exhaust に戻した。</summary>
    RecoverFromDiscard = 21,
    /// <summary>ターン開始時のエナジー追加量（互換名 EnergyMax）を増加させた。</summary>
    GainMaxEnergy      = 22,

    /// <summary>キャラクター固有スキルを使用した。</summary>
    UseCharacterSkill  = 23,
    /// <summary>コンボゲージが増減した。</summary>
    ComboGaugeChanged  = 24,
    /// <summary>カード個体へコスト増減modifierを付与した。</summary>
    ModifyCardCost     = 25,
    /// <summary>An independently controlled participant toggled Ready.</summary>
    ParticipantReadyChanged = 26,
    /// <summary>Every active participant is Ready and queued combat begins resolving.</summary>
    ResolutionStarted = 27,
    /// <summary>A Contingency card entered its armed state in the exhaust pile.</summary>
    ContingencyArmed = 28,
    /// <summary>An armed Contingency began resolving its effect list.</summary>
    ContingencyTriggered = 29,
    /// <summary>A Finisher saved its connected Attack cards and reset the current combo.</summary>
    FinisherResolved = 30,
    /// <summary>A card effect list is executed again without replaying the card itself.</summary>
    ReenactCardEffects = 31,
    /// <summary>Cards moved from the exhaust pile back to an ordinary playable pile.</summary>
    RecoverFromExhaust = 32,
    /// <summary>Block spent by a Cover provider for another ally during an enemy Attack hit.</summary>
    CoverBlockSpent = 33,
    /// <summary>A character's passive trait began resolving.</summary>
    CharacterTraitTriggered = 34,
    /// <summary>A triggered Contingency finished resolving and reached its final pile.</summary>
    ContingencyResolved = 35,
    /// <summary>The discard pile was shuffled to rebuild a depleted draw pile.</summary>
    Shuffle = 36,
    /// <summary>An actor gained an Attack in its Single, Random, or All attack pool.</summary>
    GainAttack = 37,
    /// <summary>
    /// カード制御の敵が、予約したカードの効果を実行し始めた（CardId = カード定義 ID）。
    /// 同じカードの効果が連続する間は 1 回だけ出す。演出でカード名を見せるため。
    /// </summary>
    EnemyCardAction = 38,
    /// <summary>キャラの位相が変わった（Note = 前->後:理由）。</summary>
    PhaseChanged = 39,
}
