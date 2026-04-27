using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Server.Dtos;

namespace RoguelikeCardGame.Server.Services;

/// <summary>
/// Core <see cref="BattleState"/> を Server DTO に変換する。
/// Phase 10.3-MVP spec §4-2 参照。
/// 10.2.B で実装済の <see cref="AttackPool.Display"/> / <see cref="BlockPool.Display"/>
/// を使い、力/敏捷/脱力反映後の表示値を計算する。
/// </summary>
internal static class BattleStateDtoMapper
{
    public static BattleStateDto ToDto(BattleState state) =>
        new(
            Turn: state.Turn,
            Phase: state.Phase.ToString(),
            Outcome: state.Outcome.ToString(),
            Allies: state.Allies.Select(ToActorDto).ToList(),
            Enemies: state.Enemies.Select(ToActorDto).ToList(),
            TargetAllyIndex: state.TargetAllyIndex,
            TargetEnemyIndex: state.TargetEnemyIndex,
            Energy: state.Energy,
            EnergyMax: state.EnergyMax,
            DrawPile: state.DrawPile.Select(ToCardDto).ToList(),
            Hand: state.Hand.Select(ToCardDto).ToList(),
            DiscardPile: state.DiscardPile.Select(ToCardDto).ToList(),
            ExhaustPile: state.ExhaustPile.Select(ToCardDto).ToList(),
            SummonHeld: state.SummonHeld.Select(ToCardDto).ToList(),
            PowerCards: state.PowerCards.Select(ToCardDto).ToList(),
            ComboCount: state.ComboCount,
            LastPlayedOrigCost: state.LastPlayedOrigCost,
            NextCardComboFreePass: state.NextCardComboFreePass,
            OwnedRelicIds: state.OwnedRelicIds.ToList(),
            Potions: state.Potions.ToList(),
            EncounterId: state.EncounterId);

    private static CombatActorDto ToActorDto(CombatActor a) =>
        new(
            InstanceId: a.InstanceId,
            DefinitionId: a.DefinitionId,
            Side: a.Side.ToString(),
            SlotIndex: a.SlotIndex,
            CurrentHp: a.CurrentHp,
            MaxHp: a.MaxHp,
            BlockDisplay: a.Block.Display(a.GetStatus("dexterity")),
            AttackSingleDisplay: a.AttackSingle.Display(a.GetStatus("strength"), a.GetStatus("weak")),
            AttackRandomDisplay: a.AttackRandom.Display(a.GetStatus("strength"), a.GetStatus("weak")),
            AttackAllDisplay: a.AttackAll.Display(a.GetStatus("strength"), a.GetStatus("weak")),
            Statuses: a.Statuses.ToDictionary(kv => kv.Key, kv => kv.Value),
            CurrentMoveId: a.CurrentMoveId,
            RemainingLifetimeTurns: a.RemainingLifetimeTurns,
            AssociatedSummonHeldInstanceId: a.AssociatedSummonHeldInstanceId);

    private static BattleCardInstanceDto ToCardDto(BattleCardInstance c) =>
        new(c.InstanceId, c.CardDefinitionId, c.IsUpgraded, c.CostOverride);

    public static BattleEventDto ToEventDto(BattleEvent ev) =>
        new(
            Kind: ev.Kind.ToString(),
            Order: ev.Order,
            CasterInstanceId: ev.CasterInstanceId,
            TargetInstanceId: ev.TargetInstanceId,
            Amount: ev.Amount,
            CardId: ev.CardId,
            Note: ev.Note);

    /// <summary>
    /// Phase 10.3-MVP 確定 (spec §3-5): SnapshotAfter は常に最終 state と同一。
    /// Client は CSS transition で HP/Block 数値を補間する。
    /// 将来 Phase 10.4 で本物の中間 state に進化させる場合、DTO 形式を維持して
    /// 中身だけ差し替え可能。
    /// </summary>
    public static BattleActionResponseDto ToActionResponse(
        BattleState finalState, IReadOnlyList<BattleEvent> events)
    {
        var stateDto = ToDto(finalState);
        var steps = events
            .Select(ev => new BattleEventStepDto(ToEventDto(ev), stateDto))
            .ToList();
        return new BattleActionResponseDto(stateDto, steps);
    }
}
