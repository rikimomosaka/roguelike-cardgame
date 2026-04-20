namespace RoguelikeCardGame.Core.Relics;

/// <summary>レリックの効果発動タイミング。</summary>
public enum RelicTrigger
{
    /// <summary>入手した瞬間に 1 度だけ発動し、その後は何もしない。</summary>
    OnPickup,
    /// <summary>所持している間、常に効果を発揮する。</summary>
    Passive,
    /// <summary>戦闘開始時に発動する。</summary>
    OnBattleStart,
    /// <summary>戦闘終了時に発動する。</summary>
    OnBattleEnd,
    /// <summary>マスのイベント解決後に発動する。</summary>
    OnMapTileResolved,
}
