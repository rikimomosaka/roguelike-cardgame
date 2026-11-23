namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// Identifies who submits commands for one battle participant.  Ownership and
/// control are deliberately separate: a CPU summon can still belong to a human
/// player's actor for attribution and relic rules.
/// </summary>
public enum BattleParticipantControllerKind
{
    Human = 0,
    Cpu = 1,
}
