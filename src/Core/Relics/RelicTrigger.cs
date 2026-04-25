namespace RoguelikeCardGame.Core.Relics;

/// <summary>レリックの効果発動タイミング。</summary>
public enum RelicTrigger
{
    /// <summary>入手した瞬間に 1 度だけ発動する。</summary>
    OnPickup           = 0,
    /// <summary>所持している間、常に効果を発揮する（runtime 計算は呼び出し側）。</summary>
    Passive            = 1,
    /// <summary>戦闘開始時に発動する（Phase 10.2 で発火）。</summary>
    OnBattleStart      = 2,
    /// <summary>戦闘終了時に発動する（Phase 10.2 で発火）。</summary>
    OnBattleEnd        = 3,
    /// <summary>マスのイベント解決後に発動する（NonBattleRelicEffects で発火）。</summary>
    OnMapTileResolved  = 4,
    /// <summary>各ターン開始時に発動する（Phase 10.2 で発火）。</summary>
    OnTurnStart        = 5,
    /// <summary>各ターン終了時に発動する（Phase 10.2 で発火）。</summary>
    OnTurnEnd          = 6,
    /// <summary>カードプレイ時に発動する（Phase 10.2 で発火、条件絞りは将来拡張）。</summary>
    OnCardPlay         = 7,
    /// <summary>敵撃破時に発動する（Phase 10.2 で発火）。</summary>
    OnEnemyDeath       = 8,
}
