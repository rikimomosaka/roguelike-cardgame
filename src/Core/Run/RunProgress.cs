namespace RoguelikeCardGame.Core.Run;

/// <summary>ラン 1 回分の進行状態。</summary>
public enum RunProgress
{
    /// <summary>進行中（通常）。</summary>
    InProgress = 0,
    /// <summary>ボス撃破でクリア。</summary>
    Cleared = 1,
    /// <summary>HP 0 で死亡。</summary>
    GameOver = 2,
    /// <summary>プレイヤー操作による放棄（タイトルへ戻るなど）。</summary>
    Abandoned = 3,
}
