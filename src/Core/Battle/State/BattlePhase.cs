namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>バトルの大局フェーズ。親 spec §3-1 / §4-1 参照。</summary>
public enum BattlePhase
{
    PlayerInput     = 0,
    PlayerAttacking = 1,
    EnemyAttacking  = 2,
    Resolved        = 3,
}
