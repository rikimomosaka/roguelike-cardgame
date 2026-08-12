using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.Projection;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Server.Dtos;

namespace RoguelikeCardGame.Server.Services;

/// <summary>Maps authoritative Core battle values to transport DTOs.</summary>
internal static class BattleStateDtoMapper
{
    public static BattleStateDto ToDto(BattleState state, DataCatalog data)
    {
        var hero = ProvisionalSoloHeroOwnerResolver.Resolve(state)?.Hero;
        var heroContext = BuildHeroContext(hero);
        return new BattleStateDto(
            Turn: state.Turn,
            Phase: state.Phase.ToString(),
            Outcome: state.Outcome.ToString(),
            Allies: state.Allies
                .Select(actor => ToActorDto(actor, state, data))
                .ToList(),
            Enemies: state.Enemies
                .Select(actor => ToActorDto(actor, state, data))
                .ToList(),
            TargetAllyIndex: state.TargetAllyIndex,
            TargetEnemyIndex: state.TargetEnemyIndex,
            Energy: state.Energy,
            EnergyPerTurn: state.EnergyMax,
            DrawPile: state.DrawPile
                .Select(card => ToCardDto(
                    card, data, heroContext, owner: null,
                    handState: null))
                .ToList(),
            Hand: state.Hand
                .Select(card => ToCardDto(
                    card, data, heroContext, hero,
                    handState: state))
                .ToList(),
            DiscardPile: state.DiscardPile
                .Select(card => ToCardDto(
                    card, data, heroContext, owner: null,
                    handState: null))
                .ToList(),
            ExhaustPile: state.ExhaustPile
                .Select(card => ToCardDto(
                    card, data, heroContext, owner: null,
                    handState: null))
                .ToList(),
            SummonHeld: state.SummonHeld
                .Select(card => ToCardDto(
                    card, data, heroContext, owner: null,
                    handState: null))
                .ToList(),
            PowerCards: state.PowerCards
                .Select(card => ToCardDto(
                    card, data, heroContext, owner: null,
                    handState: null))
                .ToList(),
            ComboCount: state.ComboCount,
            MaxComboThisTurn: state.MaxComboThisTurn,
            ComboGauge: state.ComboGauge,
            UsedCharacterSkillIdsThisTurn: state.UsedCharacterSkillIdsThisTurn
                .OrderBy(id => id, System.StringComparer.Ordinal)
                .ToList(),
            LastPlayedOrigCost: state.LastPlayedOrigCost,
            NextCardComboFreePass: state.NextCardComboFreePass,
            OwnedRelicIds: state.OwnedRelicIds.ToList(),
            Potions: state.Potions.ToList(),
            EncounterId: state.EncounterId,
            PendingCardPlay: state.PendingCardPlay is null
                ? null
                : new PendingCardPlayDto(
                    CardInstanceId: state.PendingCardPlay.CardInstanceId,
                    EffectIndex: state.PendingCardPlay.EffectIndex,
                    Choice: new PendingChoiceDto(
                        Action: state.PendingCardPlay.Choice.Action,
                        Pile: state.PendingCardPlay.Choice.Pile,
                        Count: state.PendingCardPlay.Choice.Count,
                        CandidateInstanceIds: state.PendingCardPlay.Choice
                            .CandidateInstanceIds.ToArray())));
    }

    private static CardActorContext BuildHeroContext(CombatActor? hero)
    {
        return hero is null
            ? CardActorContext.Empty
            : new CardActorContext(
                Strength: hero.GetStatus("strength"),
                Weak: hero.GetStatus("weak"),
                Dexterity: hero.GetStatus("dexterity"));
    }

    private static CombatActorDto ToActorDto(
        CombatActor actor,
        BattleState state,
        DataCatalog data) =>
        new(
            InstanceId: actor.InstanceId,
            DefinitionId: actor.DefinitionId,
            Side: actor.Side.ToString(),
            SlotIndex: actor.SlotIndex,
            CurrentHp: actor.CurrentHp,
            MaxHp: actor.MaxHp,
            BlockDisplay: actor.Block.Display(actor.GetStatus("dexterity")),
            AttackSingleDisplay: actor.AttackSingle.Display(
                actor.GetStatus("strength"), actor.GetStatus("weak")),
            AttackRandomDisplay: actor.AttackRandom.Display(
                actor.GetStatus("strength"), actor.GetStatus("weak")),
            AttackAllDisplay: actor.AttackAll.Display(
                actor.GetStatus("strength"), actor.GetStatus("weak")),
            Statuses: actor.Statuses.ToDictionary(pair => pair.Key, pair => pair.Value),
            CurrentMoveId: actor.CurrentMoveId,
            RemainingLifetimeTurns: actor.RemainingLifetimeTurns,
            AssociatedSummonHeldInstanceId: actor.AssociatedSummonHeldInstanceId,
            Intent: ToIntentDto(CombatIntentProjector.Project(state, actor, data)));

    private static IntentDto? ToIntentDto(CombatIntentProjection? intent) =>
        intent is null
            ? null
            : new IntentDto(
                intent.AttackSingle,
                intent.AttackRandom,
                intent.AttackAll,
                intent.AttackHits,
                intent.AttackSingleHits,
                intent.AttackRandomHits,
                intent.AttackAllHits,
                intent.Block,
                intent.HasBuff,
                intent.HasDebuff,
                intent.HasHeal);

    private static BattleCardInstanceDto ToCardDto(
        BattleCardInstance card,
        DataCatalog data,
        CardActorContext heroContext,
        CombatActor? owner,
        BattleState? handState)
    {
        string? adjustedDescription = null;
        string? adjustedUpgradedDescription = null;
        int? originalCost = null;
        int? currentCost = null;
        int? costOverride = null;

        if (data.Cards.TryGetValue(card.CardDefinitionId, out var definition))
        {
            adjustedDescription = CardTextFormatter.Format(
                definition, upgraded: false, heroContext);
            if (definition.IsUpgradable)
            {
                adjustedUpgradedDescription = CardTextFormatter.Format(
                    definition, upgraded: true, heroContext);
            }

            var cost = handState is not null && owner is not null
                ? BattleCardCostCalculator.EvaluateForHand(
                    handState, card, definition, owner)
                : BattleCardCostCalculator.Evaluate(
                    card,
                    definition,
                    owner: null,
                    isInHand: false,
                    comboReductionEligible: false);
            originalCost = cost.OriginalCost;
            currentCost = cost.PayableCost;

            // Legacy client compatibility only. Old clients apply the combo -1
            // themselves, so expose the authoritative pre-combo cost here.
            // New clients consume OriginalCost / CurrentCost and never recalculate.
            costOverride = originalCost != cost.CostBeforeCombo
                ? cost.CostBeforeCombo
                : null;
        }

        return new BattleCardInstanceDto(
            card.InstanceId,
            card.CardDefinitionId,
            card.IsUpgraded,
            costOverride,
            originalCost,
            currentCost,
            adjustedDescription,
            adjustedUpgradedDescription);
    }

    public static BattleEventDto ToEventDto(BattleEvent battleEvent) =>
        new(
            Kind: battleEvent.Kind.ToString(),
            Order: battleEvent.Order,
            CasterInstanceId: battleEvent.CasterInstanceId,
            TargetInstanceId: battleEvent.TargetInstanceId,
            Amount: battleEvent.Amount,
            CardId: battleEvent.CardId,
            Note: battleEvent.Note,
            BlockedAmount: battleEvent.BlockedAmount,
            HitIndex: battleEvent.HitIndex,
            DamageType: battleEvent.DamageType?.ToString(),
            Source: battleEvent.Source is null
                ? null
                : ToCombatSourceDto(battleEvent.Source),
            CauseChain: battleEvent.CauseChain is { } chain
                ? chain.Frames.Select(ToDamageCauseFrameDto).ToList()
                : [],
            ActualHpLoss: battleEvent.ActualHpLoss,
            Overkill: battleEvent.Overkill,
            HitLanded: battleEvent.HitLanded);

    private static CombatSourceDto ToCombatSourceDto(CombatSource source) =>
        new(
            Kind: source.Kind.ToString(),
            OwnerSlot: source.OwnerSlot,
            OwnerInstanceId: source.OwnerInstanceId,
            ActorInstanceId: source.ActorInstanceId,
            DefinitionId: source.DefinitionId,
            InstanceId: source.InstanceId,
            MoveId: source.MoveId,
            EffectIndex: source.EffectIndex,
            Scope: source.Scope);

    private static DamageCauseFrameDto ToDamageCauseFrameDto(DamageCauseFrame frame) =>
        new(
            Depth: frame.Depth,
            DamageType: frame.DamageType.ToString(),
            Source: ToCombatSourceDto(frame.Source),
            HitIndex: frame.HitIndex,
            Cause: frame.Cause);

    public static BattleActionResponseDto ToActionResponse(
        BattleState finalState,
        IReadOnlyList<BattleEvent> events,
        DataCatalog data)
    {
        var stateDto = ToDto(finalState, data);
        var steps = events
            .Select(battleEvent => new BattleEventStepDto(
                ToEventDto(battleEvent), stateDto))
            .ToList();
        return new BattleActionResponseDto(stateDto, steps);
    }
}
