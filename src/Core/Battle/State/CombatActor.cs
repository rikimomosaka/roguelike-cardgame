using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Events;

namespace RoguelikeCardGame.Core.Battle.State;

/// <summary>
/// バトル中の戦闘者状態。
/// 親 spec §3-2 参照。
/// 10.2.B で Statuses / GetStatus 追加。
/// 10.2.D で RemainingLifetimeTurns / AssociatedSummonHeldInstanceId 追加（召喚 system）。
/// 親 spec §3-2 の `AssociatedSummonHeldIndex: int?` は 10.2.D で `AssociatedSummonHeldInstanceId: string?` に訂正
/// （memory feedback ルール「InstanceId 検索」準拠、SummonHeld 配列 index ずれ問題回避）。
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
    string? CurrentMoveId,
    int? RemainingLifetimeTurns,                   // 10.2.D
    string? AssociatedSummonHeldInstanceId)        // 10.2.D
{
    /// <summary>
    /// Logical player ownership for heroes and summons. Legacy battle snapshots omit
    /// these fields; resolvers retain a solo-safe fallback for those snapshots.
    /// Enemy actors normally leave both values null.
    /// </summary>
    public int? OwnerPlayerSlot { get; init; }
    public string? OwnerActorInstanceId { get; init; }

    /// <summary>
    /// Last positive applier for each status. This init-only optional property keeps
    /// old constructors and saves compatible; absent legacy data is an empty map.
    /// </summary>
    public ImmutableDictionary<string, CombatSource> StatusSources { get; init; } =
        ImmutableDictionary<string, CombatSource>.Empty;

    public bool IsAlive => CurrentHp > 0;

    /// <summary>未保持なら 0 を返す。Statuses は 0 以下の amount を持たない不変条件。</summary>
    public int GetStatus(string id) => Statuses.TryGetValue(id, out var v) ? v : 0;

    public CombatSource? GetStatusSource(string id) =>
        Statuses.ContainsKey(id) && StatusSources.TryGetValue(id, out var source)
            ? source
            : null;
}
