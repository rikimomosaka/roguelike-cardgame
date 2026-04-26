using System.Collections.Immutable;

namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// バトル中の戦闘者状態。10.2.B で Statuses フィールドと GetStatus を追加。
/// 親 spec §3-2 / Phase 10.2.B spec §2-3 参照。
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
    ImmutableDictionary<string, int> Statuses,
    string? CurrentMoveId)
{
    public bool IsAlive => CurrentHp > 0;

    /// <summary>未保持なら 0 を返す。Statuses は 0 以下の amount を持たない不変条件。</summary>
    public int GetStatus(string id) => Statuses.TryGetValue(id, out var v) ? v : 0;
}
