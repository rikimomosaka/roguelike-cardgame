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

    /// <summary>
    /// Status icon display order. A status is appended when it is first applied,
    /// so newly gained statuses are rendered to the right of existing ones.
    /// Legacy snapshots may omit this field; projections append any missing ids.
    /// </summary>
    public ImmutableArray<string> StatusDisplayOrder { get; init; } =
        ImmutableArray<string>.Empty;

    /// <summary>
    /// カード制御の敵が今ターン予約した戦場効果（使用順）。行動予告はここから投影し、
    /// 敵の行動実行時に適用して空にする。味方・旧 Move 敵は常に空。
    /// </summary>
    public ImmutableArray<PlannedCardEffect> PlannedEffects { get; init; } =
        ImmutableArray<PlannedCardEffect>.Empty;

    /// <summary>
    /// 基礎攻撃力（定義の初期値。永続補正は今後加算）。筋力とは別の能力値で、
    /// AmountSource "selfAttack" が参照する。筋力・脱力等は従来どおりヒット解決時に加わる。
    /// </summary>
    public int AttackPower { get; init; }

    /// <summary>基礎守備力。AmountSource "selfDefense" が参照する。敏捷は従来どおり加わる。</summary>
    public int DefensePower { get; init; }

    /// <summary>位相（静・動）。バフ・デバフとは別。旧データは None。</summary>
    public ActorPhase Phase { get; init; } = ActorPhase.None;

    /// <summary>
    /// 使用時の判定で「この位相は消える」と決まり、カードの効果処理が終わるのを待っている。
    /// 効果処理中に位相を得た（同じ位相を得直した場合も含む）ら取り消される。
    /// </summary>
    public bool PhaseExitPending { get; init; }

    public bool IsAlive => CurrentHp > 0;

    /// <summary>未保持なら 0 を返す。Statuses は 0 以下の amount を持たない不変条件。</summary>
    public int GetStatus(string id) => Statuses.TryGetValue(id, out var v) ? v : 0;

    public CombatSource? GetStatusSource(string id) =>
        Statuses.ContainsKey(id) && StatusSources.TryGetValue(id, out var source)
            ? source
            : null;
}
