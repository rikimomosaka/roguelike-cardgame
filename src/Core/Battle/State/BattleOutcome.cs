namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>バトル結果。Defeat はソロモードでのみ発生（Phase 10.2.A 時点）。</summary>
public enum BattleOutcome
{
    Pending = 0,
    Victory = 1,
    Defeat  = 2,
}
