namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// バトル中の戦闘者状態。主人公 / 召喚 / 敵すべて共通。
/// 親 spec §3-2 参照。Statuses / RemainingLifetimeTurns / AssociatedSummonHeldIndex は
/// 10.2.A スコープ外。10.2.B (Statuses) / 10.2.D (Lifetime / Summon) で追加。
/// </summary>
public sealed record CombatActor(
    string InstanceId,
    string DefinitionId,
    ActorSide Side,
    int SlotIndex,
    int CurrentHp,
    int MaxHp,
    BlockPool Block,
    AttackPool AttackSingle,
    AttackPool AttackRandom,
    AttackPool AttackAll,
    string? CurrentMoveId)
{
    public bool IsAlive => CurrentHp > 0;
}
