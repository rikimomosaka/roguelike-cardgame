using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.Projection;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Server.Dtos;

namespace RoguelikeCardGame.Server.Services;

/// <summary>Maps authoritative Core battle values to transport DTOs.</summary>
internal static class BattleStateDtoMapper
{
    /// <param name="replayEnemyParticipantId">
    /// 行動再生の記録用。指定した敵参加者のカード資源を Participants に含める
    /// （ライブ状態では敵の手札を公開しないため、通常は null）。
    /// </param>
    public static BattleStateDto ToDto(
        BattleState state,
        DataCatalog data,
        string? replayEnemyParticipantId = null)
    {
        var primary = BattleParticipantAccess.GetPrimary(state);
        var hero = state.Allies.FirstOrDefault(actor => string.Equals(
                actor.InstanceId,
                primary.ActorInstanceId,
                System.StringComparison.Ordinal))
            ?? ProvisionalSoloHeroOwnerResolver.Resolve(state)?.Hero;
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
            EnergyPerTurn: (int)System.Math.Clamp(
                (long)state.EnergyMax + System.Math.Max(0, hero?.GetStatus(BattleStatusIds.Haste) ?? 0),
                0L,
                int.MaxValue),
            DrawPile: state.DrawPile
                .Select(card => ToCardDto(
                    card, data, heroContext, hero, state, isInHand: false))
                .ToList(),
            Hand: state.Hand
                .Select(card => ToCardDto(
                    card, data, heroContext, hero, state, isInHand: true))
                .ToList(),
            DiscardPile: state.DiscardPile
                .Select(card => ToCardDto(
                    card, data, heroContext, hero, state, isInHand: false))
                .ToList(),
            ExhaustPile: state.ExhaustPile
                .Select(card => ToCardDto(
                    card, data, heroContext, hero, state,
                    isInHand: false, isInExhaust: true))
                .ToList(),
            SummonHeld: state.SummonHeld
                .Select(card => ToCardDto(
                    card, data, heroContext, hero, state, isInHand: false))
                .ToList(),
            PowerCards: state.PowerCards
                .Select(card => ToCardDto(
                    card, data, heroContext, hero, state, isInHand: false))
                .ToList(),
            ComboCount: state.ComboCount,
            MaxComboThisTurn: state.MaxComboThisTurn,
            ComboGauge: state.ComboGauge,
            UsedCharacterSkillIdsThisTurn: state.UsedCharacterSkillIdsThisTurn
                .OrderBy(id => id, System.StringComparer.Ordinal)
                .ToList(),
            CharacterSkillUsesThisTurn: state.CharacterSkillUsesThisTurn
                .OrderBy(pair => pair.Key, System.StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, System.StringComparer.Ordinal),
            LastPlayedOrigCost: state.LastPlayedOrigCost,
            NextCardComboFreePass: state.NextCardComboFreePass,
            OwnedRelicIds: state.OwnedRelicIds.ToList(),
            Potions: state.Potions.ToList(),
            PotionUsesThisTurn: state.PotionUsesThisTurn
                .OrderBy(pair => pair.Key, System.StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, System.StringComparer.Ordinal),
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
                            .CandidateInstanceIds.ToArray())),
            CharacterSkillsRestricted: hero?.GetStatus(BattleStatusIds.Seal) > 0,
            ActionRevision: state.ActionRevision,
            Participants: ParticipantDtos(state, data, replayEnemyParticipantId),
            FinisherRecord: ToFinisherRecordDto(state, primary, hero, data),
            ComboCarryoverPending: state.ComboCarryoverPending,
            EnemyParticipants: state.EnemyParticipants.IsDefaultOrEmpty
                ? Array.Empty<EnemyParticipantSummaryDto>()
                : state.EnemyParticipants
                    .Select(participant => new EnemyParticipantSummaryDto(
                        participant.ParticipantId,
                        participant.ActorInstanceId,
                        participant.Energy,
                        participant.Hand.Length,
                        participant.DrawPile.Length,
                        participant.DiscardPile.Length,
                        participant.ExhaustPile.Length,
                        participant.EnergyPerTurn,
                        participant.DrawPerTurn))
                    .ToList(),
            PlayHistoryThisTurn: ToPlayHistoryDto(primary, hero, data));
    }

    /// <summary>このターンに使用したカード（実際のプレイのみ、使用順）。</summary>
    private static IReadOnlyList<FinisherRecordCardDto> ToPlayHistoryDto(
        BattleParticipantState participant,
        CombatActor? owner,
        DataCatalog data)
    {
        if (participant.PlayedCardHistoryThisTurn.IsDefaultOrEmpty)
            return Array.Empty<FinisherRecordCardDto>();
        var context = BuildHeroContext(owner);
        return participant.PlayedCardHistoryThisTurn
            .OrderBy(snapshot => snapshot.PlaySequence)
            .Select(snapshot => new FinisherRecordCardDto(
                PlaySequence: snapshot.PlaySequence,
                Card: ToFinisherCardDto(snapshot, data, context)))
            .ToList();
    }

    private static List<BattleParticipantDto> ParticipantDtos(
        BattleState state,
        DataCatalog data,
        string? replayEnemyParticipantId)
    {
        var list = BattleParticipantAccess.EffectiveParticipants(state)
            .Select(participant => ToParticipantDto(state, participant, data))
            .ToList();
        if (replayEnemyParticipantId is not null
            && BattleParticipantAccess.TryGetEnemy(
                state, replayEnemyParticipantId, out var enemyParticipant))
        {
            list.Add(ToParticipantDto(state, enemyParticipant, data, isEnemy: true));
        }
        return list;
    }

    private static BattleParticipantDto ToParticipantDto(
        BattleState state,
        BattleParticipantState participant,
        DataCatalog data,
        bool isEnemy = false)
    {
        // Cost, combo and usage projections still use the battle engine's root
        // compatibility fields. Project only for this DTO calculation; the
        // authoritative state itself is never mutated.
        var projected = isEnemy
            ? BattleParticipantAccess.ProjectEnemyToRoot(state, participant.ParticipantId)
            : BattleParticipantAccess.ProjectToRoot(state, participant.ParticipantId);
        var owner = (isEnemy ? state.Enemies : state.Allies).FirstOrDefault(actor =>
            string.Equals(
                actor.InstanceId,
                participant.ActorInstanceId,
                System.StringComparison.Ordinal));
        var context = BuildHeroContext(owner);

        IReadOnlyList<BattleCardInstanceDto> Cards(
            System.Collections.Immutable.ImmutableArray<BattleCardInstance> cards,
            bool isInHand,
            bool isInExhaust = false) => cards
                .Select(card => ToCardDto(
                    card, data, context, owner, projected, isInHand, isInExhaust))
                .ToList();

        return new BattleParticipantDto(
            ParticipantId: participant.ParticipantId,
            ActorInstanceId: participant.ActorInstanceId,
            ControllerKind: participant.ControllerKind.ToString(),
            OwnerPlayerSlot: participant.OwnerPlayerSlot,
            CharacterId: participant.CharacterId,
            TargetAllyIndex: participant.TargetAllyIndex,
            TargetEnemyIndex: participant.TargetEnemyIndex,
            Energy: participant.Energy,
            EnergyPerTurn: (int)System.Math.Clamp(
                (long)participant.EnergyPerTurn
                    + System.Math.Max(0, owner?.GetStatus(BattleStatusIds.Haste) ?? 0),
                0L,
                int.MaxValue),
            DrawPerTurn: participant.DrawPerTurn,
            DrawPile: Cards(participant.DrawPile, isInHand: false),
            Hand: Cards(participant.Hand, isInHand: true),
            DiscardPile: Cards(participant.DiscardPile, isInHand: false),
            ExhaustPile: Cards(
                participant.ExhaustPile, isInHand: false, isInExhaust: true),
            SummonHeld: Cards(participant.SummonHeld, isInHand: false),
            PowerCards: Cards(participant.PowerCards, isInHand: false),
            ComboCount: participant.ComboCount,
            MaxComboThisTurn: participant.MaxComboThisTurn,
            ComboGauge: participant.ComboGauge,
            LastPlayedOrigCost: participant.LastPlayedOrigCost,
            NextCardComboFreePass: participant.NextCardComboFreePass,
            WildUsedInCurrentCombo: participant.WildUsedInCurrentCombo,
            UsedCharacterSkillIdsThisTurn: participant.UsedCharacterSkillIdsThisTurn
                .OrderBy(id => id, System.StringComparer.Ordinal)
                .ToList(),
            CharacterSkillUsesThisTurn: participant.CharacterSkillUsesThisTurn
                .OrderBy(pair => pair.Key, System.StringComparer.Ordinal)
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    System.StringComparer.Ordinal),
            CardUsesThisTurn: participant.CardUsesThisTurn
                .OrderBy(pair => pair.Key, System.StringComparer.Ordinal)
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    System.StringComparer.Ordinal),
            CardUseLockedThisTurn: participant.CardUseLockedThisTurn,
            OwnedRelicIds: participant.OwnedRelicIds.ToList(),
            Potions: participant.Potions.ToList(),
            PotionUsesThisTurn: participant.PotionUsesThisTurn
                .OrderBy(pair => pair.Key, System.StringComparer.Ordinal)
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    System.StringComparer.Ordinal),
            PendingCardPlay: ToPendingDto(participant.PendingCardPlay),
            CharacterSkillsRestricted: owner?.GetStatus(BattleStatusIds.Seal) > 0,
            CpuCardPlaysThisTurn: participant.CpuCardPlaysThisTurn,
            IsReady: participant.IsReady,
            FinisherRecord: ToFinisherRecordDto(
                state, participant, owner, data),
            ComboCarryoverPending: participant.ComboCarryoverPending,
            PlayHistoryThisTurn: ToPlayHistoryDto(participant, owner, data));
    }

    private static FinisherRecordDto? ToFinisherRecordDto(
        BattleState state,
        BattleParticipantState participant,
        CombatActor? owner,
        DataCatalog data)
    {
        if (participant.LastFinisherRecord.IsDefaultOrEmpty)
            return null;

        var context = BuildHeroContext(owner);
        var cards = participant.LastFinisherRecord
            .OrderBy(snapshot => snapshot.PlaySequence)
            .Select(snapshot => new FinisherRecordCardDto(
                PlaySequence: snapshot.PlaySequence,
                Card: ToFinisherCardDto(snapshot, data, context)))
            .ToList();
        var projection = owner is null
            ? null
            : AfterimageIntentProjector.Analyze(state, owner);
        return new FinisherRecordDto(
            AfterimageStacks: System.Math.Max(
                0, owner?.GetStatus(BattleStatusIds.Afterimage) ?? 0),
            Cards: cards,
            PlannedAfterimageIntent: projection is null
                ? null
                : ToIntentDto(projection.Intent),
            IsPlannedAfterimageIntentAvailable: projection?.Availability switch
            {
                AfterimageIntentProjectionAvailability.Available => true,
                AfterimageIntentProjectionAvailability.Unavailable => false,
                _ => null,
            })
        {
            // 終撃が保存した記録か（保存カードに終撃カード自身が含まれる）。
            // 通常の「最後のアタック」保存は UI で強調表示しない。
            SavedByFinisher = participant.LastFinisherRecord.Any(snapshot =>
                data.Cards.TryGetValue(snapshot.CardDefinitionId, out var definition)
                && definition.EffectiveAbilities(snapshot.IsUpgraded)
                    .OfType<FinisherAbilityDefinition>()
                    .Any()),
        };
    }

    private static BattleReplayCardDto ToFinisherCardDto(
        PlayedCardEffectSnapshot snapshot,
        DataCatalog data,
        CardActorContext context)
    {
        data.TryGetCard(snapshot.CardDefinitionId, out var definition);
        return new BattleReplayCardDto(
            CardInstanceId: snapshot.CardInstanceId,
            DefinitionId: snapshot.CardDefinitionId,
            DisplayName: definition?.DisplayName
                ?? definition?.Name
                ?? snapshot.CardDefinitionId,
            CardType: snapshot.CardType.ToString(),
            Rarity: definition?.Rarity.ToString() ?? "Unknown",
            IsUpgraded: snapshot.IsUpgraded,
            OriginalCost: snapshot.OriginalCost,
            CurrentCost: snapshot.OriginalCost,
            PaidCost: snapshot.PaidCost,
            // The saved effect/keyword snapshot is authoritative. Catalog data
            // can change after the play and must not rewrite a Finisher record.
            Description: CardTextFormatter.FormatEffectsWithKeywords(
                snapshot.Effects,
                snapshot.Keywords,
                context),
            CostChanges: [],
            PoolClass: snapshot.EffectivePoolClass(definition).ToString());
    }

    private static PendingCardPlayDto? ToPendingDto(PendingCardPlay? pending) =>
        pending is null
            ? null
            : new PendingCardPlayDto(
                CardInstanceId: pending.CardInstanceId,
                EffectIndex: pending.EffectIndex,
                Choice: new PendingChoiceDto(
                    Action: pending.Choice.Action,
                    Pile: pending.Choice.Pile,
                    Count: pending.Choice.Count,
                    CandidateInstanceIds: pending.Choice.CandidateInstanceIds.ToArray()));

    private static CardActorContext BuildHeroContext(CombatActor? hero)
    {
        return hero is null
            ? CardActorContext.Empty
            : new CardActorContext(
                Strength: hero.GetStatus("strength"),
                Weak: hero.GetStatus("weak"),
                Dexterity: hero.GetStatus("dexterity"))
            {
                AttackPower = hero.AttackPower,
                DefensePower = hero.DefensePower,
            };
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
            Intent: ToIntentDto(CombatIntentProjector.Project(state, actor, data)),
            StatusOrder: BuildStatusOrder(actor),
            BlockAddCount: actor.Block.AddCount,
            BlockRawSum: actor.Block.Sum,
            Phase: PhaseRules.Name(actor.Phase),
            AttackPower: actor.AttackPower,
            DefensePower: actor.DefensePower);

    private static IReadOnlyList<string> BuildStatusOrder(CombatActor actor)
    {
        var active = actor.Statuses.Keys.ToHashSet(System.StringComparer.Ordinal);
        var result = actor.StatusDisplayOrder
            .Where(active.Contains)
            .Distinct(System.StringComparer.Ordinal)
            .ToList();
        var included = result.ToHashSet(System.StringComparer.Ordinal);
        result.AddRange(actor.Statuses.Keys
            .Where(id => included.Add(id))
            .OrderBy(id => id, System.StringComparer.Ordinal));
        return result;
    }

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
        BattleState battleState,
        bool isInHand,
        bool isInExhaust = false)
    {
        string? adjustedDescription = null;
        string? adjustedUpgradedDescription = null;
        int? originalCost = null;
        int? currentCost = null;
        int? costOverride = null;
        bool comboReductionEligible = false;
        bool comboContinues = false;
        bool comboRingHighlighted = false;
        bool wildDisabled = false;
        bool cardUsageLimitReached = false;
        string? cardPlayRestrictionReason = null;
        data.Cards.TryGetValue(card.CardDefinitionId, out var definition);
        string? poolClass = card.EffectivePoolClass(definition).ToString();
        IReadOnlyList<CardCostChangeDto> costChanges = [];
        var armedContingency = isInExhaust
            ? battleState.ArmedContingencies.FirstOrDefault(armed =>
                string.Equals(
                    armed.CardInstanceId,
                    card.InstanceId,
                    System.StringComparison.Ordinal))
            : null;

        if (definition is not null)
        {
            var usageCount = CardUsageTracker.Count(battleState, definition.Id);
            cardUsageLimitReached = CardUsageTracker.HasReachedLimit(
                battleState,
                definition.Id,
                definition.EffectiveUsesPerTurn(card.IsUpgraded));
            cardPlayRestrictionReason = owner is not null
                ? BattleCardCostCalculator.GetPlayRestrictionReason(
                    battleState, owner, definition)
                : null;

            var augmentedKeywords = card.AugmentedKeywords();
            adjustedDescription = CardTextFormatter.FormatWithUsageCount(
                definition, upgraded: false, heroContext, usageCount, augmentedKeywords);
            if (definition.IsUpgradable)
            {
                adjustedUpgradedDescription = CardTextFormatter.FormatWithUsageCount(
                    definition, upgraded: true, heroContext, usageCount, augmentedKeywords);
            }

            var cost = isInHand && owner is not null
                ? BattleCardCostCalculator.EvaluateForHand(
                    battleState, card, definition, owner)
                : BattleCardCostCalculator.Evaluate(
                    card,
                    definition,
                    owner: null,
                    isInHand: false,
                    comboReductionEligible: false);
            originalCost = cost.OriginalCost;
            currentCost = cost.PayableCost;
            comboReductionEligible = cost.ComboReductionEligible;
            comboContinues = cost.ComboContinues;
            comboRingHighlighted = cost.ComboRingHighlighted;
            costChanges = cost.Changes
                .Select(change => new CardCostChangeDto(
                    Kind: change.Kind switch
                    {
                        CardCostChangeKind.Fixed => "fixed",
                        CardCostChangeKind.Modifier => "modifier",
                        CardCostChangeKind.Status => "status",
                        CardCostChangeKind.Combo => "combo",
                        _ => "unknown",
                    },
                    Value: change.Value,
                    SourceId: change.SourceId,
                    SourceName: ResolveCostChangeSourceName(
                        change, battleState, data)))
                .ToList();
            var keywords = definition.EffectiveKeywords(card.IsUpgraded);
            wildDisabled = keywords?.Contains("wild") == true
                && keywords?.Contains("superwild") != true
                && battleState.WildUsedInCurrentCombo;

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
            adjustedUpgradedDescription,
            comboReductionEligible,
            comboContinues,
            comboRingHighlighted,
            wildDisabled,
            cardUsageLimitReached,
            CardPlayRestricted: cardPlayRestrictionReason is not null,
            CardPlayRestrictionReason: cardPlayRestrictionReason,
            CostChanges: costChanges,
            PoolClass: poolClass,
            IsContingencyArmed: armedContingency is not null,
            ContingencyTriggerText: armedContingency is null
                ? null
                : FormatContingencyTrigger(armedContingency.Trigger),
            RetainsAtTurnEnd: definition is not null
                && BattleCardRetention.RetainsAtTurnEnd(
                    card, definition, owner?.Phase ?? ActorPhase.None));
    }

    private static string FormatContingencyTrigger(
        ContingencyTriggerDefinition trigger) => trigger.Timing switch
    {
        ContingencyTriggerTiming.TurnStart => "ターン開始時",
        ContingencyTriggerTiming.BeforeEnemyAttack => "相手のアタック時",
        ContingencyTriggerTiming.ComboReached => trigger.ComboThreshold is int threshold
            ? $"{threshold}コンボ達成時"
            : "コンボ達成時",
        _ => trigger.Timing.ToString(),
    };

    private static string? ResolveCostChangeSourceName(
        CardCostChangeStep change,
        BattleState state,
        DataCatalog data)
    {
        if (change.Kind == CardCostChangeKind.Combo)
            return "コンボ";

        if (change.SourceId is not { Length: > 0 } sourceId)
            return change.Kind == CardCostChangeKind.Fixed ? "固定効果" : "効果";

        if (change.Kind is CardCostChangeKind.Fixed or CardCostChangeKind.Status
            && BattleStatusTextCatalog.All.TryGetValue(sourceId, out var statusText))
        {
            return statusText.Name;
        }

        var sourceCard = EnumerateBattleCards(state)
            .FirstOrDefault(card => card.InstanceId == sourceId);
        if (sourceCard is not null
            && data.Cards.TryGetValue(sourceCard.CardDefinitionId, out var sourceDefinition))
        {
            string name = sourceDefinition.DisplayName ?? sourceDefinition.Name;
            return sourceCard.IsUpgraded ? $"{name}+" : name;
        }

        if (data.Cards.TryGetValue(sourceId, out var cardDefinition))
            return cardDefinition.DisplayName ?? cardDefinition.Name;

        foreach (var character in data.Characters.Values)
        {
            var skill = character.EffectiveSkills.FirstOrDefault(skill => skill.Id == sourceId);
            if (skill is not null) return skill.Name;
        }
        foreach (var unit in data.Units?.Values
                     ?? Enumerable.Empty<UnitDefinition>())
        {
            var skill = unit.EffectiveSkills.FirstOrDefault(skill => skill.Id == sourceId);
            if (skill is not null) return skill.Name;
        }

        if (data.Relics.TryGetValue(sourceId, out var relic)) return relic.Name;
        if (data.Potions.TryGetValue(sourceId, out var potion)) return potion.Name;
        return sourceId;
    }

    private static IEnumerable<BattleCardInstance> EnumerateBattleCards(
        BattleState state)
    {
        // A modifier may be created by a different participant's card. The
        // projected compatibility root only contains the target participant,
        // while Participants retains every independently owned zone.
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var card in EnumerateRootCards(state).Concat(
                     BattleParticipantAccess.EffectiveParticipants(state)
                         .SelectMany(EnumerateParticipantCards)))
        {
            if (seen.Add(card.InstanceId)) yield return card;
        }
    }

    private static IEnumerable<BattleCardInstance> EnumerateRootCards(
        BattleState state) => state.DrawPile
        .Concat(state.Hand)
        .Concat(state.DiscardPile)
        .Concat(state.ExhaustPile)
        .Concat(state.SummonHeld)
        .Concat(state.PowerCards);

    private static IEnumerable<BattleCardInstance> EnumerateParticipantCards(
        BattleParticipantState participant) => participant.DrawPile
        .Concat(participant.Hand)
        .Concat(participant.DiscardPile)
        .Concat(participant.ExhaustPile)
        .Concat(participant.SummonHeld)
        .Concat(participant.PowerCards);

    public static BattleEventDto ToEventDto(
        BattleEvent battleEvent,
        BattleReplayCardDto? card = null) =>
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
            HitLanded: battleEvent.HitLanded,
            Card: card,
            ComboCountAfter: battleEvent.ComboCountAfter);

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
                ToEventDto(
                    battleEvent,
                    BattleReplayDtoMapper.ToCardDto(
                        BattleActionJournalComposer.SnapshotReenactedCard(
                            battleEvent, data))),
                stateDto))
            .ToList();
        return new BattleActionResponseDto(stateDto, steps);
    }
}
