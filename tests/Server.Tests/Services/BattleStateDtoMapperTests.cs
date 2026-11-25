using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Server.Dtos;
using RoguelikeCardGame.Server.Services;
using RoguelikeCardGame.Server.Tests.Controllers;
using RoguelikeCardGame.Server.Tests.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Server.Tests.Services;

/// <summary>
/// Phase 10.5.C: hero (caster) の statuses が CardActorContext として
/// formatter に渡され、各 pile のカードの AdjustedDescription / AdjustedUpgradedDescription
/// が populate されることを integration レベルで検証する。
/// </summary>
public class BattleStateDtoMapperTests : IClassFixture<TempDataFactory>
{
    private readonly TempDataFactory _factory;

    public BattleStateDtoMapperTests(TempDataFactory factory) => _factory = factory;

    [Fact]
    public async Task Finisher_record_projects_snapshot_faces_and_afterimage_intent()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "dto-finisher-record");
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);
            var store = _factory.Services.GetRequiredService<BattleSessionStore>();
            var catalog = _factory.Services.GetRequiredService<DataCatalog>();
            Assert.True(store.TryGet(accountId, out var session));
            var participant = BattleParticipantAccess.GetPrimary(session.State);
            var card = participant.Hand.First();
            var definition = catalog.Cards[card.CardDefinitionId];
            var effects = ImmutableArray.Create(
                new CardEffect(
                    "attack",
                    EffectScope.Random,
                    EffectSide.Enemy,
                    13));
            var snapshot = new PlayedCardEffectSnapshot(
                definition.Id,
                card.InstanceId,
                participant.ActorInstanceId,
                CardType.Attack,
                OriginalCost: 3,
                PaidCost: 2,
                IsUpgraded: true,
                Keywords: ["finisher"],
                Effects: effects,
                PlaySequence: 41);
            participant = participant with
            {
                LastFinisherRecord = [snapshot],
            };
            var state = BattleParticipantAccess.Replace(session.State, participant);
            int heroIndex = state.Allies.IndexOf(state.Allies.First(actor =>
                actor.InstanceId == participant.ActorInstanceId));
            var hero = state.Allies[heroIndex];
            hero = hero with
            {
                Statuses = hero.Statuses.SetItem(BattleStatusIds.Afterimage, 2),
            };
            state = state with
            {
                Allies = state.Allies.SetItem(heroIndex, hero),
            };

            var dto = BattleStateDtoMapper.ToDto(state, catalog);

            var rootRecord = Assert.IsType<FinisherRecordDto>(dto.FinisherRecord);
            var participantRecord = Assert.IsType<FinisherRecordDto>(
                Assert.Single(dto.Participants!).FinisherRecord);
            Assert.Equal(2, rootRecord.AfterimageStacks);
            Assert.True(rootRecord.IsPlannedAfterimageIntentAvailable);
            Assert.Equal(26, rootRecord.PlannedAfterimageIntent!.AttackRandom);
            Assert.Equal(2, rootRecord.PlannedAfterimageIntent.AttackRandomHits);
            var saved = Assert.Single(rootRecord.Cards);
            Assert.Equal(41, saved.PlaySequence);
            Assert.Equal(card.InstanceId, saved.Card.CardInstanceId);
            Assert.True(saved.Card.IsUpgraded);
            Assert.Equal(3, saved.Card.OriginalCost);
            Assert.Equal(2, saved.Card.PaidCost);
            Assert.Equal(
                CardTextFormatter.FormatEffectsWithKeywords(
                    effects, snapshot.Keywords, CardActorContext.Empty),
                saved.Card.Description);
            Assert.Equal(rootRecord.AfterimageStacks, participantRecord.AfterimageStacks);
            Assert.Equal(
                rootRecord.PlannedAfterimageIntent,
                participantRecord.PlannedAfterimageIntent);
            Assert.Equal(
                rootRecord.IsPlannedAfterimageIntentAvailable,
                participantRecord.IsPlannedAfterimageIntentAvailable);
            Assert.Equal(
                rootRecord.Cards.Select(entry => entry.PlaySequence),
                participantRecord.Cards.Select(entry => entry.PlaySequence));

            // 終撃カードを含まない保存（全クラス共通の「最後のアタック」）は強調対象外。
            Assert.Equal(
                definition.EffectiveAbilities(true).OfType<FinisherAbilityDefinition>().Any(),
                rootRecord.SavedByFinisher);
            var finisherSnapshot = snapshot with
            {
                CardDefinitionId = "striker_epic_flash_finale",
            };
            var finisherState = BattleParticipantAccess.Replace(
                state,
                BattleParticipantAccess.GetPrimary(state) with
                {
                    LastFinisherRecord = [finisherSnapshot],
                });
            Assert.True(BattleStateDtoMapper.ToDto(finisherState, catalog)
                .FinisherRecord!.SavedByFinisher);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Combo_carryover_pending_projects_to_root_and_participant_DTOs()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "dto-combo-carryover-pending");
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);
            var store = _factory.Services.GetRequiredService<BattleSessionStore>();
            var catalog = _factory.Services.GetRequiredService<DataCatalog>();
            Assert.True(store.TryGet(accountId, out var session));
            var primary = BattleParticipantAccess.GetPrimary(session.State) with
            {
                ComboCarryoverPending = true,
            };
            var state = BattleParticipantAccess.Replace(session.State, primary);

            var dto = BattleStateDtoMapper.ToDto(state, catalog);

            Assert.True(dto.ComboCarryoverPending);
            Assert.True(Assert.Single(dto.Participants!).ComboCarryoverPending);
            var json = JsonSerializer.SerializeToElement(
                dto,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.True(json.GetProperty("comboCarryoverPending").GetBoolean());
            Assert.True(json.GetProperty("participants")[0]
                .GetProperty("comboCarryoverPending").GetBoolean());
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Card_pool_class_is_projected_in_every_battle_card_dto()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "dto-card-pool-class");
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);
            var store = _factory.Services.GetRequiredService<BattleSessionStore>();
            var baseCatalog = _factory.Services.GetRequiredService<DataCatalog>();
            Assert.True(store.TryGet(accountId, out var session));
            var sourceCard = session.State.Hand[0];
            var definition = baseCatalog.Cards[sourceCard.CardDefinitionId] with
            {
                PoolClass = CardPoolClass.Guardian,
            };
            var catalog = baseCatalog with
            {
                Cards = new Dictionary<string, CardDefinition>(baseCatalog.Cards)
                {
                    [definition.Id] = definition,
                },
            };

            var dto = BattleStateDtoMapper.ToDto(session.State, catalog);

            Assert.All(
                dto.Hand.Where(card => card.CardDefinitionId == definition.Id),
                card => Assert.Equal("Guardian", card.PoolClass));
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Reenact_event_projects_the_exact_effect_only_card_face()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "dto-reenacted-card");
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);
            var store = _factory.Services.GetRequiredService<BattleSessionStore>();
            var catalog = _factory.Services.GetRequiredService<DataCatalog>();
            Assert.True(store.TryGet(accountId, out var session));
            var participant = BattleParticipantAccess.GetPrimary(session.State);
            var sourceCard = participant.Hand[0] with { IsUpgraded = true };
            participant = participant with
            {
                Hand = participant.Hand.RemoveAt(0),
                DiscardPile = participant.DiscardPile.Add(sourceCard),
            };
            var state = BattleParticipantAccess.Replace(session.State, participant);
            var marker = new BattleEvent(
                BattleEventKind.ReenactCardEffects,
                Order: 0,
                CasterInstanceId: participant.ActorInstanceId,
                CardId: sourceCard.CardDefinitionId,
                Source: new CombatSource(
                    CombatSourceKind.CardEffect,
                    OwnerSlot: participant.OwnerPlayerSlot,
                    OwnerInstanceId: participant.ActorInstanceId,
                    ActorInstanceId: participant.ActorInstanceId,
                    DefinitionId: sourceCard.CardDefinitionId,
                    InstanceId: sourceCard.InstanceId,
                    Scope: "reenact"),
                ReenactedCard: new PlayedCardEffectSnapshot(
                    sourceCard.CardDefinitionId,
                    sourceCard.InstanceId,
                    participant.ActorInstanceId,
                    catalog.Cards[sourceCard.CardDefinitionId].CardType,
                    catalog.Cards[sourceCard.CardDefinitionId].UpgradedCost
                        ?? catalog.Cards[sourceCard.CardDefinitionId].Cost
                        ?? 0,
                    0,
                    IsUpgraded: true,
                    ImmutableArray<string>.Empty,
                    (catalog.Cards[sourceCard.CardDefinitionId].UpgradedEffects
                        ?? catalog.Cards[sourceCard.CardDefinitionId].Effects)
                        .ToImmutableArray(),
                    1));

            var response = BattleStateDtoMapper.ToActionResponse(
                state, [marker], catalog);

            var eventDto = Assert.Single(response.Steps).Event;
            Assert.Equal(sourceCard.CardDefinitionId, eventDto.CardId);
            Assert.Equal(sourceCard.InstanceId, eventDto.Card!.CardInstanceId);
            Assert.True(eventDto.Card.IsUpgraded);
            Assert.Equal(
                catalog.Cards[sourceCard.CardDefinitionId].PoolClass?.ToString(),
                eventDto.Card.PoolClass);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Status_restrictions_are_projected_for_cards_and_character_skills()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "dto-status-restrictions");
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);
            var store = _factory.Services.GetRequiredService<BattleSessionStore>();
            var catalog = _factory.Services.GetRequiredService<DataCatalog>();
            Assert.True(store.TryGet(accountId, out var session));
            int attackIndex = Enumerable.Range(0, session.State.Hand.Length)
                .FirstOrDefault(index =>
                    catalog.Cards[session.State.Hand[index].CardDefinitionId].CardType
                        == CardType.Attack,
                    -1);
            Assert.True(attackIndex >= 0);
            var hero = session.State.Allies[0] with
            {
                Statuses = session.State.Allies[0].Statuses
                    .SetItem(BattleStatusIds.Bind, 1)
                    .SetItem(BattleStatusIds.Seal, 1),
            };
            var state = session.State with
            {
                Allies = session.State.Allies.SetItem(0, hero),
            };

            var dto = BattleStateDtoMapper.ToDto(state, catalog);

            Assert.True(dto.CharacterSkillsRestricted);
            Assert.True(dto.Hand[attackIndex].CardPlayRestricted);
            Assert.Equal(BattleStatusIds.Bind,
                dto.Hand[attackIndex].CardPlayRestrictionReason);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Card_definition_projects_shared_usage_counter_and_limit_state()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "dto-card-usage-limit");
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);
            var store = _factory.Services.GetRequiredService<BattleSessionStore>();
            var baseCatalog = _factory.Services.GetRequiredService<DataCatalog>();
            Assert.True(store.TryGet(accountId, out var session));
            var card = session.State.Hand[0];
            var definition = baseCatalog.Cards[card.CardDefinitionId] with
            {
                UsesPerTurn = 1,
            };
            var cards = new Dictionary<string, CardDefinition>(baseCatalog.Cards)
            {
                [definition.Id] = definition,
            };
            var state = session.State with
            {
                CardUsesThisTurn = ImmutableDictionary<string, int>.Empty
                    .Add(definition.Id, 1),
                PotionUsesThisTurn = ImmutableDictionary<string, int>.Empty
                    .Add("block_potion", 1),
            };

            var dto = BattleStateDtoMapper.ToDto(state, baseCatalog with { Cards = cards });
            var projected = Assert.Single(dto.Hand, item => item.InstanceId == card.InstanceId);

            Assert.True(projected.CardUsageLimitReached);
            Assert.StartsWith(
                "ターン中1度だけ使用可能(1/1)。",
                projected.AdjustedDescription);
            Assert.Equal(1, dto.PotionUsesThisTurn["block_potion"]);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Every_card_zone_projects_shared_usage_and_actor_order_plus_haste_recovery()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "dto-all-zone-live-state");
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);
            var store = _factory.Services.GetRequiredService<BattleSessionStore>();
            var baseCatalog = _factory.Services.GetRequiredService<DataCatalog>();
            Assert.True(store.TryGet(accountId, out var session));
            var sourceCard = session.State.Hand[0];
            var definition = baseCatalog.Cards[sourceCard.CardDefinitionId] with
            {
                CardType = CardType.Attack,
                Keywords = new[] { "wild" },
                UpgradedKeywords = new[] { "wild" },
                UsesPerTurn = 1,
                UpgradedUsesPerTurn = 1,
            };
            var catalog = baseCatalog with
            {
                Cards = new Dictionary<string, CardDefinition>(baseCatalog.Cards)
                {
                    [definition.Id] = definition,
                },
            };
            BattleCardInstance Copy(string zone, bool upgraded = false) =>
                sourceCard with { InstanceId = $"zone-{zone}", IsUpgraded = upgraded };
            var hero = session.State.Allies[0] with
            {
                Statuses = ImmutableDictionary<string, int>.Empty
                    .Add(BattleStatusIds.Weak, 1)
                    .Add(BattleStatusIds.Haste, 2)
                    .Add(BattleStatusIds.Bind, 1),
                StatusDisplayOrder = ImmutableArray.Create(
                    BattleStatusIds.Weak, BattleStatusIds.Haste, BattleStatusIds.Bind),
            };
            var state = session.State with
            {
                Allies = session.State.Allies.SetItem(0, hero),
                DrawPile = ImmutableArray.Create(Copy("draw")),
                Hand = ImmutableArray.Create(Copy("hand", upgraded: true)),
                DiscardPile = ImmutableArray.Create(Copy("discard")),
                ExhaustPile = ImmutableArray.Create(Copy("exhaust")),
                SummonHeld = ImmutableArray.Create(Copy("summon")),
                PowerCards = ImmutableArray.Create(Copy("power")),
                CardUsesThisTurn = ImmutableDictionary<string, int>.Empty
                    .Add(definition.Id, 1),
                WildUsedInCurrentCombo = true,
            };

            var dto = BattleStateDtoMapper.ToDto(state, catalog);
            var projectedCards = dto.DrawPile
                .Concat(dto.Hand)
                .Concat(dto.DiscardPile)
                .Concat(dto.ExhaustPile)
                .Concat(dto.SummonHeld)
                .Concat(dto.PowerCards)
                .ToArray();

            Assert.Equal(6, projectedCards.Length);
            Assert.All(projectedCards, card =>
            {
                Assert.True(card.CardUsageLimitReached);
                Assert.True(card.CardPlayRestricted);
                Assert.Equal(BattleStatusIds.Bind, card.CardPlayRestrictionReason);
                Assert.True(card.WildDisabled);
                Assert.StartsWith(
                    "ターン中1度だけ使用可能(1/1)。",
                    card.AdjustedDescription);
            });
            Assert.Equal(state.EnergyMax + 2, dto.EnergyPerTurn);
            Assert.Equal(
                new[] { BattleStatusIds.Weak, BattleStatusIds.Haste, BattleStatusIds.Bind },
                dto.Allies[0].StatusOrder);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Armed_contingency_is_marked_only_on_its_exhaust_card()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "dto-armed-contingency");
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);
            var store = _factory.Services.GetRequiredService<BattleSessionStore>();
            var catalog = _factory.Services.GetRequiredService<DataCatalog>();
            Assert.True(store.TryGet(accountId, out var session));
            var card = session.State.Hand[0];
            var primary = BattleParticipantAccess.GetPrimary(session.State) with
            {
                Hand = session.State.Hand
                    .Where(candidate => candidate.InstanceId != card.InstanceId)
                    .ToImmutableArray(),
                ExhaustPile = session.State.ExhaustPile.Add(card),
                ArmedContingencies =
                [
                    new ArmedContingency(
                        card.InstanceId,
                        session.State.Allies[0].InstanceId,
                        new ContingencyTriggerDefinition(
                            ContingencyTriggerTiming.ComboReached,
                            ComboThreshold: 3),
                        ArmedOrdinal: 1),
                ],
            };
            var state = BattleParticipantAccess.Replace(session.State, primary);
            var secondaryActor = session.State.Allies[0] with
            {
                InstanceId = "dto-contingency-secondary-actor",
                SlotIndex = 1,
                OwnerPlayerSlot = 1,
            };
            var secondaryCard = card with
            {
                InstanceId = "dto-secondary-armed-contingency",
            };
            state = state with { Allies = state.Allies.Add(secondaryActor) };
            state = BattleParticipantAccess.Add(
                state,
                primary with
                {
                    ParticipantId = "cpu:dto-contingency-secondary",
                    ActorInstanceId = secondaryActor.InstanceId,
                    ControllerKind = BattleParticipantControllerKind.Cpu,
                    OwnerPlayerSlot = 1,
                    Hand = [],
                    DrawPile = [],
                    DiscardPile = [],
                    ExhaustPile = [secondaryCard],
                    SummonHeld = [],
                    PowerCards = [],
                    ArmedContingencies =
                    [
                        new ArmedContingency(
                            secondaryCard.InstanceId,
                            secondaryActor.InstanceId,
                            new ContingencyTriggerDefinition(
                                ContingencyTriggerTiming.BeforeEnemyAttack),
                            ArmedOrdinal: 2),
                    ],
                });

            var dto = BattleStateDtoMapper.ToDto(state, catalog);

            var rootCard = Assert.Single(dto.ExhaustPile, item =>
                item.InstanceId == card.InstanceId);
            Assert.True(rootCard.IsContingencyArmed);
            Assert.Equal("3コンボ達成時", rootCard.ContingencyTriggerText);
            var participant = Assert.Single(dto.Participants!, item =>
                item.ParticipantId == primary.ParticipantId);
            var participantCard = Assert.Single(participant.ExhaustPile, item =>
                item.InstanceId == card.InstanceId);
            Assert.True(participantCard.IsContingencyArmed);
            Assert.Equal("3コンボ達成時", participantCard.ContingencyTriggerText);
            var secondaryDto = Assert.Single(dto.Participants!, item =>
                item.ActorInstanceId == secondaryActor.InstanceId);
            var secondaryDtoCard = Assert.Single(secondaryDto.ExhaustPile);
            Assert.True(secondaryDtoCard.IsContingencyArmed);
            Assert.Equal("相手のアタック時", secondaryDtoCard.ContingencyTriggerText);

            var replay = BattleReplayDtoMapper.ToDto(
                BattleActionJournal.Empty.AppendSuccessfulCommand(
                    new BattleActionDraft(
                        Turn: state.Turn,
                        CommandId: "snapshot-armed-contingency",
                        Kind: "playCard",
                        ParticipantId: primary.ParticipantId,
                        ActorInstanceId: primary.ActorInstanceId,
                        RevisionBefore: state.ActionRevision,
                        RevisionAfter: state.ActionRevision + 1,
                        StateBefore: dto,
                        StateAfter: dto)),
                new BattleActionFilter(
                    Turn: state.Turn,
                    ParticipantId: primary.ParticipantId,
                    ActorInstanceId: primary.ActorInstanceId));
            var replayCard = Assert.Single(
                Assert.Single(replay.Entries).StateAfter!.Participants!
                    .Single(item => item.ParticipantId == primary.ParticipantId)
                    .ExhaustPile,
                item => item.InstanceId == card.InstanceId);
            Assert.True(replayCard.IsContingencyArmed);
            Assert.Equal("3コンボ達成時", replayCard.ContingencyTriggerText);
            var replaySecondaryCard = Assert.Single(
                Assert.Single(replay.Entries).StateAfter!.Participants!
                    .Single(item => item.ActorInstanceId == secondaryActor.InstanceId)
                    .ExhaustPile);
            Assert.True(replaySecondaryCard.IsContingencyArmed);
            Assert.Equal("相手のアタック時", replaySecondaryCard.ContingencyTriggerText);

            // A stale registry entry must never paint a card outside Exhaust as armed.
            primary = BattleParticipantAccess.GetPrimary(state) with
            {
                ExhaustPile = primary.ExhaustPile
                    .Where(candidate => candidate.InstanceId != card.InstanceId)
                    .ToImmutableArray(),
                Hand = primary.Hand.Add(card),
            };
            var movedDto = BattleStateDtoMapper.ToDto(
                BattleParticipantAccess.Replace(state, primary), catalog);
            var movedCard = Assert.Single(movedDto.Hand, item =>
                item.InstanceId == card.InstanceId);
            Assert.False(movedCard.IsContingencyArmed);
            Assert.Null(movedCard.ContingencyTriggerText);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Intent_uses_Core_projection_for_ordered_self_buff_then_attack()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "dto-core-intent-projection");
        try
        {
            var start = await client.PostAsync("/api/v1/runs/current/battle/start", null);
            start.EnsureSuccessStatusCode();

            var services = _factory.Services;
            var store = services.GetRequiredService<BattleSessionStore>();
            var baseCatalog = services.GetRequiredService<DataCatalog>();
            Assert.True(store.TryGet(accountId, out var session));
            var move = new MoveDefinition(
                "buff_then_attack",
                MoveKind.Multi,
                new CardEffect[]
                {
                    new("buff", EffectScope.Self, null, 2, Name: "strength"),
                    new("attack", EffectScope.All, EffectSide.Enemy, 10),
                },
                "buff_then_attack");
            var definition = new EnemyDefinition(
                "dto_projection_enemy",
                "DTO Projection Enemy",
                "dto_projection_enemy",
                Hp: 30,
                Pool: new EnemyPool(1, EnemyTier.Weak),
                InitialMoveId: move.Id,
                Moves: new[] { move });
            var enemies = new Dictionary<string, EnemyDefinition>(baseCatalog.Enemies)
            {
                [definition.Id] = definition,
            };
            var catalog = baseCatalog with { Enemies = enemies };
            var projectedEnemy = session.State.Enemies[0] with
            {
                DefinitionId = definition.Id,
                CurrentMoveId = move.Id,
                Statuses = ImmutableDictionary<string, int>.Empty,
                // 旧 Move 敵として投影する（カード制御の予約は持たない）。
                PlannedEffects = ImmutableArray<PlannedCardEffect>.Empty,
            };
            var state = session.State with
            {
                Enemies = session.State.Enemies.SetItem(0, projectedEnemy),
            };

            var dto = BattleStateDtoMapper.ToDto(state, catalog);
            var intent = Assert.Single(
                dto.Enemies, actor => actor.InstanceId == projectedEnemy.InstanceId).Intent;

            Assert.NotNull(intent);
            Assert.True(intent!.HasBuff);
            Assert.Equal(12, intent.AttackAll);
            Assert.Equal(1, intent.AttackAllHits);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Cost_modifiers_do_not_expose_cost_null_card_as_playable()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "dto-ultimate-unplayable");
        try
        {
            var start = await client.PostAsync("/api/v1/runs/current/battle/start", null);
            start.EnsureSuccessStatusCode();

            var services = _factory.Services;
            var store = services.GetRequiredService<BattleSessionStore>();
            var baseCatalog = services.GetRequiredService<DataCatalog>();
            Assert.True(store.TryGet(accountId, out var session));

            var unplayableDefinition = new CardDefinition(
                "unplayable_status",
                "Unplayable Status",
                DisplayName: null,
                CardRarity.Token,
                CardType.Status,
                Cost: null,
                UpgradedCost: null,
                Effects: System.Array.Empty<CardEffect>(),
                UpgradedEffects: null,
                Keywords: null);
            var cards = new Dictionary<string, CardDefinition>(baseCatalog.Cards)
            {
                [unplayableDefinition.Id] = unplayableDefinition,
            };
            var catalog = baseCatalog with { Cards = cards };
            var card = new BattleCardInstance(
                "unplayable-status-instance",
                unplayableDefinition.Id,
                IsUpgraded: false,
                CostOverride: 5,
                CostModifiers: ImmutableArray.Create(new CardCostModifier(
                    Amount: -5,
                    CardCostModifierDuration.UntilTurnEnd,
                    SourceId: "ultimate")));
            var state = session.State with
            {
                Hand = session.State.Hand.Add(card),
            };

            var dto = BattleStateDtoMapper.ToDto(state, catalog);
            var projectedCard = Assert.Single(
                dto.Hand, candidate => candidate.InstanceId == card.InstanceId);

            Assert.Null(projectedCard.CostOverride);
            Assert.Null(projectedCard.OriginalCost);
            Assert.Null(projectedCard.CurrentCost);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Card_cost_projection_uses_Core_rules_for_hand_and_excludes_hand_rules_from_other_piles()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "dto-authoritative-card-cost");
        try
        {
            (await client.PostAsync("/api/v1/runs/current/battle/start", null))
                .EnsureSuccessStatusCode();

            var services = _factory.Services;
            var store = services.GetRequiredService<BattleSessionStore>();
            var baseCatalog = services.GetRequiredService<DataCatalog>();
            Assert.True(store.TryGet(accountId, out var session));
            var definition = new CardDefinition(
                Id: "dto_cost_five",
                Name: "DTO Cost Five",
                DisplayName: null,
                Rarity: CardRarity.Common,
                CardType: CardType.Skill,
                Cost: 5,
                UpgradedCost: null,
                Effects: System.Array.Empty<CardEffect>(),
                UpgradedEffects: null,
                Keywords: null);
            var catalog = baseCatalog with
            {
                Cards = new Dictionary<string, CardDefinition>(baseCatalog.Cards)
                {
                    [definition.Id] = definition,
                },
            };
            var modifier = new CardCostModifier(
                Amount: -1,
                CardCostModifierDuration.UntilPlayed,
                SourceId: "dto-source-card");
            var handCard = new BattleCardInstance(
                "dto-hand-cost", definition.Id, false, CostOverride: 5,
                CostModifiers: ImmutableArray.Create(modifier));
            var drawCard = new BattleCardInstance(
                "dto-draw-cost", definition.Id, false, null,
                ImmutableArray.Create(modifier));
            var sourceCard = new BattleCardInstance(
                "dto-source-card", definition.Id, true, null);
            var hero = session.State.Allies[0] with
            {
                Statuses = session.State.Allies[0].Statuses
                    .SetItem("handCostIncrease", 1)
                    .SetItem("handCostReduction", 2),
            };
            var state = session.State with
            {
                Allies = session.State.Allies.SetItem(0, hero),
                Hand = ImmutableArray.Create(handCard),
                DrawPile = ImmutableArray.Create(drawCard),
                DiscardPile = ImmutableArray.Create(sourceCard),
                LastPlayedOrigCost = 4,
                NextCardComboFreePass = false,
            };

            var dto = BattleStateDtoMapper.ToDto(state, catalog);
            var projectedHand = Assert.Single(dto.Hand);
            var projectedDraw = Assert.Single(dto.DrawPile);

            Assert.Equal(5, projectedHand.OriginalCost);
            Assert.Equal(2, projectedHand.CurrentCost); // 5 - 1 + 1 - 2 - combo 1
            Assert.True(projectedHand.ComboReductionEligible);
            Assert.Equal(3, projectedHand.CostOverride); // legacy alias is pre-combo
            Assert.Collection(
                projectedHand.CostChanges!,
                change =>
                {
                    Assert.Equal("fixed", change.Kind);
                    Assert.Equal(5, change.Value);
                    Assert.Equal("混乱", change.SourceName);
                },
                change =>
                {
                    Assert.Equal("modifier", change.Kind);
                    Assert.Equal(-1, change.Value);
                    Assert.Equal("DTO Cost Five+", change.SourceName);
                },
                change =>
                {
                    Assert.Equal("status", change.Kind);
                    Assert.Equal(1, change.Value);
                    Assert.Equal("浪費", change.SourceName);
                },
                change =>
                {
                    Assert.Equal("status", change.Kind);
                    Assert.Equal(-2, change.Value);
                    Assert.Equal("節制", change.SourceName);
                },
                change =>
                {
                    Assert.Equal("combo", change.Kind);
                    Assert.Equal(-1, change.Value);
                    Assert.Equal("コンボ", change.SourceName);
                });
            Assert.Equal(5, projectedDraw.OriginalCost);
            Assert.Equal(4, projectedDraw.CurrentCost); // only the per-card modifier applies
            Assert.False(projectedDraw.ComboReductionEligible);
            Assert.Equal(4, projectedDraw.CostOverride);
            var drawChange = Assert.Single(projectedDraw.CostChanges!);
            Assert.Equal("DTO Cost Five+", drawChange.SourceName);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Participant_cost_sources_resolve_cards_and_unit_skills_across_owners()
    {
        var (client, accountId) = await BattleControllerFixtures
            .SetupRunWithActiveBattleAsync(
                _factory, accountId: "dto-cross-participant-cost-source");
        try
        {
            (await client.PostAsync("/api/v1/runs/current/battle/start", null))
                .EnsureSuccessStatusCode();
            var services = _factory.Services;
            var store = services.GetRequiredService<BattleSessionStore>();
            var baseCatalog = services.GetRequiredService<DataCatalog>();
            Assert.True(store.TryGet(accountId, out var session));
            Assert.NotNull(baseCatalog.Units);
            var baseUnits = baseCatalog.Units!;

            var definition = new CardDefinition(
                Id: "dto_shared_source_card",
                Name: "Shared Source",
                DisplayName: null,
                Rarity: CardRarity.Common,
                CardType: CardType.Skill,
                Cost: 3,
                UpgradedCost: null,
                Effects: [],
                UpgradedEffects: null,
                Keywords: null);
            var skill = new CharacterSkillDefinition(
                "dto_unit_cost_skill",
                "Unit Cost Skill",
                new ResourceCost(),
                OncePerTurn: false,
                Effects: []);
            var unit = baseUnits["wisp"] with { Skills = [skill] };
            var catalog = baseCatalog with
            {
                Cards = new Dictionary<string, CardDefinition>(baseCatalog.Cards)
                {
                    [definition.Id] = definition,
                },
                Units = new Dictionary<string, UnitDefinition>(baseUnits)
                {
                    [unit.Id] = unit,
                },
            };

            var sourceCard = new BattleCardInstance(
                "other-owner-source-instance", definition.Id,
                IsUpgraded: true, CostOverride: null);
            var targetCard = new BattleCardInstance(
                "secondary-target-instance", definition.Id,
                IsUpgraded: false, CostOverride: null,
                CostModifiers:
                [
                    new CardCostModifier(
                        -1, CardCostModifierDuration.UntilPlayed,
                        sourceCard.InstanceId),
                    new CardCostModifier(
                        -1, CardCostModifierDuration.UntilPlayed,
                        skill.Id),
                ]);
            var secondaryActor = session.State.Allies[0] with
            {
                InstanceId = "dto-secondary-actor",
                DefinitionId = unit.Id,
                SlotIndex = 1,
                OwnerActorInstanceId = session.State.Allies[0].InstanceId,
            };
            var state = session.State with
            {
                Allies = session.State.Allies.Add(secondaryActor),
            };
            var primary = BattleParticipantAccess.GetPrimary(state) with
            {
                DiscardPile = [sourceCard],
            };
            state = BattleParticipantAccess.Replace(state, primary);
            var secondary = primary with
            {
                ParticipantId = "cpu:dto-secondary-actor",
                ActorInstanceId = secondaryActor.InstanceId,
                ControllerKind = BattleParticipantControllerKind.Cpu,
                CharacterId = unit.Id,
                TargetAllyIndex = 1,
                Hand = [targetCard],
                DrawPile = [],
                DiscardPile = [],
                ExhaustPile = [],
                SummonHeld = [],
                PowerCards = [],
                IsReady = false,
            };
            state = BattleParticipantAccess.Add(state, secondary);

            var dto = BattleStateDtoMapper.ToDto(state, catalog);
            var projected = Assert.Single(dto.Participants!, participant =>
                participant.ParticipantId == secondary.ParticipantId);
            var changes = Assert.Single(projected.Hand).CostChanges!;

            Assert.Equal(
                "Shared Source+",
                Assert.Single(changes, change =>
                    change.SourceId == sourceCard.InstanceId).SourceName);
            Assert.Equal(
                skill.Name,
                Assert.Single(changes, change =>
                    change.SourceId == skill.Id).SourceName);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Ultimate_and_hand_projection_use_same_lowest_slot_living_hero_owner()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "dto-provisional-solo-owner");
        try
        {
            (await client.PostAsync("/api/v1/runs/current/battle/start", null))
                .EnsureSuccessStatusCode();

            var services = _factory.Services;
            var store = services.GetRequiredService<BattleSessionStore>();
            var baseCatalog = services.GetRequiredService<DataCatalog>();
            Assert.True(store.TryGet(accountId, out var session));

            var definition = new CardDefinition(
                Id: "dto_owner_cost_five",
                Name: "DTO Owner Cost Five",
                DisplayName: null,
                Rarity: CardRarity.Common,
                CardType: CardType.Skill,
                Cost: 5,
                UpgradedCost: null,
                Effects: System.Array.Empty<CardEffect>(),
                UpgradedEffects: null,
                Keywords: null);
            var catalog = baseCatalog with
            {
                Cards = new Dictionary<string, CardDefinition>(baseCatalog.Cards)
                {
                    [definition.Id] = definition,
                },
            };
            var firstHighSlotHero = session.State.Allies[0] with
            {
                InstanceId = "hero-high-slot",
                SlotIndex = 5,
                Statuses = ImmutableDictionary<string, int>.Empty,
            };
            var secondLowSlotHero = session.State.Allies[0] with
            {
                InstanceId = "hero-low-slot",
                SlotIndex = 1,
                Statuses = ImmutableDictionary<string, int>.Empty.SetItem(
                    BattleCardCostCalculator.HandCostReductionStatusId, 2),
            };
            var card = new BattleCardInstance(
                "dto-owner-hand-card", definition.Id, false, null);
            var primary = session.State.Participants[0] with
            {
                ActorInstanceId = secondLowSlotHero.InstanceId,
                TargetAllyIndex = 1,
            };
            var state = session.State with
            {
                Allies = ImmutableArray.Create(firstHighSlotHero, secondLowSlotHero),
                Participants = session.State.Participants.SetItem(0, primary),
                Hand = ImmutableArray.Create(card),
                ComboGauge = BattleEngine.UltimateSkillGaugeCost,
                ComboCount = 0,
                MaxComboThisTurn = 0,
                LastPlayedOrigCost = null,
                NextCardComboFreePass = false,
            };

            var (afterUltimate, events) = BattleEngine.UseUltimateSkill(state, catalog);
            var modifier = Assert.Single(afterUltimate.Hand[0].EffectiveCostModifiers);
            Assert.Equal(-3, modifier.Amount); // base 5 - low-slot owner's reduction 2
            var ultimate = Assert.Single(
                events, battleEvent => battleEvent.Kind == BattleEventKind.UseCharacterSkill);
            Assert.Equal("hero-low-slot", ultimate.CasterInstanceId);

            var dto = BattleStateDtoMapper.ToDto(afterUltimate, catalog);
            var projected = Assert.Single(dto.Hand);

            Assert.Equal(5, projected.OriginalCost);
            Assert.Equal(0, projected.CurrentCost);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Battle_state_DTO_exposes_turn_max_combo_with_camel_case_name()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "dto-turn-max-combo");
        try
        {
            var start = await client.PostAsync("/api/v1/runs/current/battle/start", null);
            start.EnsureSuccessStatusCode();

            var store = _factory.Services.GetRequiredService<BattleSessionStore>();
            Assert.True(store.TryGet(accountId, out var session));
            store.Set(accountId, session with
            {
                State = session.State with
                {
                    ComboCount = 1,
                    MaxComboThisTurn = 4,
                    UsedCharacterSkillIdsThisTurn = ImmutableHashSet.Create("tactical"),
                },
            });

            var response = await client.GetAsync("/api/v1/runs/current/battle");
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            Assert.Equal(1, json.RootElement.GetProperty("comboCount").GetInt32());
            Assert.Equal(4, json.RootElement.GetProperty("maxComboThisTurn").GetInt32());
            Assert.Equal(
                "tactical",
                json.RootElement.GetProperty("usedCharacterSkillIdsThisTurn")[0]
                    .GetString());
            Assert.Equal(
                session.State.EnergyMax,
                json.RootElement.GetProperty("energyPerTurn").GetInt32());
            Assert.False(json.RootElement.TryGetProperty("energyMax", out _));
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public void ToEventDto_preserves_structured_damage_attribution()
    {
        var source = new CombatSource(
            CombatSourceKind.CardEffect,
            OwnerSlot: 2,
            OwnerInstanceId: "hero-2",
            ActorInstanceId: "summon-2",
            DefinitionId: "strike",
            InstanceId: "strike-instance",
            EffectIndex: 1,
            Scope: "random");
        var chain = new DamageCauseChain(ImmutableArray.Create(
            new DamageCauseFrame(0, DamageType.Attack, source, 3, "attack")));
        var ev = new BattleEvent(
            BattleEventKind.DealDamage,
            Order: 7,
            CasterInstanceId: "summon-2",
            TargetInstanceId: "enemy-1",
            Amount: 12,
            HitIndex: 3,
            DamageType: DamageType.Attack,
            Source: source,
            CauseChain: chain,
            ActualHpLoss: 9,
            Overkill: 3,
            HitLanded: true);

        var dto = BattleStateDtoMapper.ToEventDto(ev);

        Assert.Equal("Attack", dto.DamageType);
        Assert.Equal(2, dto.Source!.OwnerSlot);
        Assert.Equal("random", dto.Source.Scope);
        Assert.Equal("strike-instance", dto.Source.InstanceId);
        var cause = Assert.Single(dto.CauseChain);
        Assert.Equal("CardEffect", cause.Source.Kind);
        Assert.Equal(3, cause.HitIndex);
        Assert.Equal(9, dto.ActualHpLoss);
        Assert.Equal(3, dto.Overkill);
        Assert.True(dto.HitLanded is true);
    }

    [Fact]
    public void ToEventDto_does_not_assign_unattributed_events_to_player_zero()
    {
        var dto = BattleStateDtoMapper.ToEventDto(new BattleEvent(
            BattleEventKind.DealDamage,
            Order: 0,
            Amount: 1,
            Source: CombatSource.Unattributed));

        Assert.NotNull(dto.Source);
        Assert.Equal("Unattributed", dto.Source.Kind);
        Assert.Null(dto.Source.OwnerSlot);
    }

    [Fact]
    public void ToEventDto_preserves_post_play_combo_count_as_a_typed_field()
    {
        var dto = BattleStateDtoMapper.ToEventDto(new BattleEvent(
            BattleEventKind.PlayCard,
            Order: 0,
            CasterInstanceId: "hero-1",
            CardId: "finisher",
            ComboCountAfter: 12));

        Assert.Equal(12, dto.ComboCountAfter);
    }

    [Fact]
    public void ToEventDto_projects_gain_attack_without_losing_its_scope()
    {
        var dto = BattleStateDtoMapper.ToEventDto(new BattleEvent(
            BattleEventKind.GainAttack,
            Order: 2,
            CasterInstanceId: "hero-1",
            TargetInstanceId: "hero-1",
            Amount: 7,
            Note: "random"));

        Assert.Equal("GainAttack", dto.Kind);
        Assert.Equal("hero-1", dto.CasterInstanceId);
        Assert.Equal("hero-1", dto.TargetInstanceId);
        Assert.Equal(7, dto.Amount);
        Assert.Equal("random", dto.Note);
    }

    [Fact]
    public async Task BattleStart_no_buff_emits_unmodified_marker_in_hand()
    {
        var (client, _) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "ctx-no-buff");
        try
        {
            var res = await client.PostAsync("/api/v1/runs/current/battle/start", null);
            res.EnsureSuccessStatusCode();
            var body = await res.Content.ReadFromJsonAsync<BattleActionResponseDto>();
            Assert.NotNull(body);

            // hero に buff 無し → 攻撃の基本 (attack 6 single) は無修飾 [N:6] のまま。
            var allCards = body!.State.Hand
                .Concat(body.State.DrawPile)
                .Concat(body.State.DiscardPile)
                .ToList();
            Assert.NotEmpty(allCards);
            // 全カードに adjustedDescription が populate されている。
            Assert.All(allCards, c =>
            {
                Assert.NotNull(c.AdjustedDescription);
                Assert.DoesNotContain("|up]", c.AdjustedDescription);
                Assert.DoesNotContain("|down]", c.AdjustedDescription);
            });

            var strike = allCards.FirstOrDefault(c => c.CardDefinitionId == "striker_basic_attack");
            Assert.NotNull(strike);
            Assert.Equal("敵単体に[R:6|:自身の攻撃力]アタック。", strike!.AdjustedDescription);
            // 待機等を持たない札はターン終了で手札に残らない（ドロー演出の引き直し判定用）。
            Assert.False(strike.RetainsAtTurnEnd);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Hero_with_strength_emits_up_marker_in_hand_descriptions()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "ctx-strength");
        try
        {
            // session を作るために start を呼ぶ
            await client.PostAsync("/api/v1/runs/current/battle/start", null);

            // hero に strength=2 を直接付与し、再度 GET で adjustedDescription を確認
            ApplyHeroStatus(_factory.Services, accountId, "strength", 2);

            var res = await client.GetAsync("/api/v1/runs/current/battle");
            res.EnsureSuccessStatusCode();
            var state = await res.Content.ReadFromJsonAsync<BattleStateDto>();
            Assert.NotNull(state);

            var allCards = state!.Hand
                .Concat(state.DrawPile)
                .Concat(state.DiscardPile)
                .ToList();
            var strike = allCards.FirstOrDefault(c => c.CardDefinitionId == "striker_basic_attack");
            Assert.NotNull(strike);
            // 6 + 2 = 8、base 6 と異なるので up 修飾。
            // Phase 10.5.M6.4: 数字 marker 前後 space 全削除
            Assert.Equal("敵単体に[R:8|up:自身の攻撃力]アタック。", strike!.AdjustedDescription);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Hero_with_weak_emits_down_marker_in_hand_descriptions()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "ctx-weak");
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);
            ApplyHeroStatus(_factory.Services, accountId, "weak", 1);

            var res = await client.GetAsync("/api/v1/runs/current/battle");
            res.EnsureSuccessStatusCode();
            var state = await res.Content.ReadFromJsonAsync<BattleStateDto>();
            Assert.NotNull(state);

            var allCards = state!.Hand
                .Concat(state.DrawPile)
                .Concat(state.DiscardPile)
                .ToList();
            var strike = allCards.FirstOrDefault(c => c.CardDefinitionId == "striker_basic_attack");
            Assert.NotNull(strike);
            // 6 * 0.75 = 4.5 → floor 4、base 6 と異なるので down 修飾。
            // Phase 10.5.M6.4: 数字 marker 前後 space 全削除
            Assert.Equal("敵単体に[R:4|down:自身の攻撃力]アタック。", strike!.AdjustedDescription);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Hero_with_dexterity_emits_up_on_block_cards()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "ctx-dex");
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);
            ApplyHeroStatus(_factory.Services, accountId, "dexterity", 3);

            var res = await client.GetAsync("/api/v1/runs/current/battle");
            res.EnsureSuccessStatusCode();
            var state = await res.Content.ReadFromJsonAsync<BattleStateDto>();
            Assert.NotNull(state);

            var allCards = state!.Hand
                .Concat(state.DrawPile)
                .Concat(state.DiscardPile)
                .ToList();
            var defend = allCards.FirstOrDefault(c => c.CardDefinitionId == "striker_basic_defense");
            Assert.NotNull(defend);
            // dexterity=3 で 5 + 3 = 8、base 5 と異なるので up 修飾。
            Assert.Contains("|up:自身の守備力]", defend!.AdjustedDescription);
        }
        finally
        {
            client.Dispose();
        }
    }

    /// <summary>
    /// session 内の hero に status を直接付与するヘルパー。
    /// engine 経路を通さずに statuses を更新するため、formatter context のみを試験できる。
    /// </summary>
    private static void ApplyHeroStatus(
        System.IServiceProvider services, string accountId, string statusId, int amount)
    {
        var store = services.GetRequiredService<BattleSessionStore>();
        if (!store.TryGet(accountId, out var session))
            throw new System.InvalidOperationException(
                $"ApplyHeroStatus: session not found for accountId={accountId}");
        var newAllies = session.State.Allies.Select(a =>
        {
            if (a.DefinitionId != "hero") return a;
            var newStatuses = a.Statuses.SetItem(statusId, amount);
            return a with { Statuses = newStatuses };
        }).ToImmutableArray();
        var newState = session.State with { Allies = newAllies };
        store.Set(accountId, session with { State = newState });
    }

    private static void UpdateHero(
        System.IServiceProvider services,
        string accountId,
        System.Func<CombatActor, CombatActor> update)
    {
        var store = services.GetRequiredService<BattleSessionStore>();
        if (!store.TryGet(accountId, out var session))
            throw new System.InvalidOperationException(
                $"UpdateHero: session not found for accountId={accountId}");
        var allies = session.State.Allies
            .Select(actor => actor.DefinitionId == "hero" ? update(actor) : actor)
            .ToImmutableArray();
        store.Set(accountId, session with
        {
            State = session.State with { Allies = allies },
        });
    }

    [Fact]
    public async Task Hero_intent_reports_real_hit_count_and_per_hit_weak_total()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "intent-real-hits");
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);
            UpdateHero(_factory.Services, accountId, hero => hero with
            {
                AttackSingle = AttackPool.Empty.Add(5).Add(5),
                Statuses = hero.Statuses.SetItem("weak", 1),
            });

            var state = await client.GetFromJsonAsync<BattleStateDto>(
                "/api/v1/runs/current/battle");
            var intent = Assert.Single(state!.Allies, actor => actor.DefinitionId == "hero").Intent;

            Assert.NotNull(intent);
            Assert.Equal(2, intent!.AttackHits);
            Assert.Equal(2, intent.AttackSingleHits);
            Assert.Null(intent.AttackRandomHits);
            Assert.Null(intent.AttackAllHits);
            Assert.Equal(6, intent.AttackSingle); // floor(5 * .75) を各hitへ適用: 3 + 3
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Hero_omnistrike_intent_exposes_every_hit_as_all()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "intent-omnistrike");
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);
            UpdateHero(_factory.Services, accountId, hero => hero with
            {
                AttackSingle = AttackPool.Empty.Add(5).Add(5),
                AttackRandom = AttackPool.Empty.Add(3),
                Statuses = hero.Statuses.SetItem("omnistrike", 1),
            });

            var state = await client.GetFromJsonAsync<BattleStateDto>(
                "/api/v1/runs/current/battle");
            var intent = Assert.Single(state!.Allies, actor => actor.DefinitionId == "hero").Intent;

            Assert.NotNull(intent);
            Assert.Equal(3, intent!.AttackHits);
            Assert.Equal(3, intent.AttackAllHits);
            Assert.Null(intent.AttackSingleHits);
            Assert.Null(intent.AttackRandomHits);
            Assert.Equal(13, intent.AttackAll);
            Assert.Null(intent.AttackSingle);
            Assert.Null(intent.AttackRandom);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task EndTurn_event_dto_carries_exact_block_and_hit_index()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "event-hit-metadata");
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);
            var store = _factory.Services.GetRequiredService<BattleSessionStore>();
            Assert.True(store.TryGet(accountId, out var session));

            var hero = session.State.Allies[0] with
            {
                AttackSingle = AttackPool.Empty.Add(10),
            };
            var enemy = session.State.Enemies[0] with
            {
                Block = BlockPool.Empty.Add(4),
                Statuses = session.State.Enemies[0].Statuses.SetItem("vulnerable", 1),
            };
            var stagedState = session.State with
            {
                Allies = session.State.Allies.SetItem(0, hero),
                Enemies = session.State.Enemies.SetItem(0, enemy),
                TargetEnemyIndex = 0,
            };
            var catalog = _factory.Services.GetRequiredService<DataCatalog>();
            var stagedDto = BattleStateDtoMapper.ToDto(stagedState, catalog);
            Assert.Equal(1, stagedDto.Enemies[0].BlockAddCount);
            Assert.Equal(4, stagedDto.Enemies[0].BlockRawSum);
            store.Set(accountId, session with
            {
                State = stagedState,
            });

            var primary = BattleParticipantAccess.GetPrimary(session.State);
            var response = await client.PostAsJsonAsync(
                "/api/v1/runs/current/battle/end-turn",
                new BattleCommandRequestDto(
                    primary.ParticipantId,
                    primary.ActorInstanceId,
                    "mapper-legacy-end-turn",
                    session.State.ActionRevision));
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<BattleActionResponseDto>();
            var damage = Assert.Single(body!.Steps, step =>
                step.Event.Kind == "DealDamage" &&
                step.Event.CasterInstanceId == hero.InstanceId &&
                step.Event.TargetInstanceId == enemy.InstanceId);

            Assert.Equal(9, damage.Event.Amount);
            Assert.Equal(4, damage.Event.BlockedAmount);
            Assert.Equal(0, damage.Event.HitIndex);
            Assert.Equal("Attack", damage.Event.DamageType);
            Assert.NotNull(damage.Event.Source);
            Assert.Equal("Unattributed", damage.Event.Source!.Kind);
            Assert.Equal(hero.InstanceId, damage.Event.CasterInstanceId);
            var cause = Assert.Single(damage.Event.CauseChain);
            Assert.Equal("Attack", cause.DamageType);
            Assert.Equal(0, cause.HitIndex);
            Assert.Equal(9, damage.Event.ActualHpLoss);
            Assert.Equal(0, damage.Event.Overkill);
            Assert.True(damage.Event.HitLanded);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Card_controlled_summon_intent_uses_queued_attack_scope()
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "intent-summon-scope");
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);
            var store = _factory.Services.GetRequiredService<BattleSessionStore>();
            Assert.True(store.TryGet(accountId, out var session));
            var hero = Assert.Single(session.State.Allies);
            var wisp = hero with
            {
                InstanceId = "wisp-intent-test",
                DefinitionId = "wisp",
                SlotIndex = 1,
                CurrentMoveId = null,
                AttackSingle = AttackPool.Empty.Add(4),
                AttackRandom = AttackPool.Empty,
                AttackAll = AttackPool.Empty,
                Statuses = ImmutableDictionary<string, int>.Empty,
            };
            store.Set(accountId, session with
            {
                State = session.State with { Allies = session.State.Allies.Add(wisp) },
            });

            var state = await client.GetFromJsonAsync<BattleStateDto>(
                "/api/v1/runs/current/battle");
            var intent = Assert.Single(state!.Allies, actor => actor.InstanceId == wisp.InstanceId).Intent;

            Assert.NotNull(intent);
            Assert.Equal(4, intent!.AttackSingle);
            Assert.Null(intent.AttackAll);
            Assert.Equal(1, intent.AttackHits);
            Assert.Equal(1, intent.AttackSingleHits);
            Assert.Null(intent.AttackRandomHits);
            Assert.Null(intent.AttackAllHits);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Theory]
    [InlineData("goblin_c", "enemy_goblin_c_throw_knife", 8, 2)]
    [InlineData("six_ghost", "enemy_six_ghost_divider", 36, 6)]
    public async Task Enemy_multihit_intent_reports_scope_total_and_real_hit_count(
        string definitionId, string cardId, int expectedTotal, int expectedHits)
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: $"intent-enemy-{definitionId}");
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);
            var store = _factory.Services.GetRequiredService<BattleSessionStore>();
            Assert.True(store.TryGet(accountId, out var session));
            // カード制御の敵: 固有カード（旧 Move を変換したもの）を予約した状態の予告。
            var catalog = _factory.Services.GetRequiredService<DataCatalog>();
            var card = catalog.Cards[cardId];
            var enemy = session.State.Enemies[0] with
            {
                DefinitionId = definitionId,
                CurrentMoveId = null,
                Statuses = ImmutableDictionary<string, int>.Empty,
                PlannedEffects = card.Effects
                    .Select(effect => new PlannedCardEffect(
                        effect, RoguelikeCardGame.Core.Battle.Events.CombatSource.Unattributed))
                    .ToImmutableArray(),
            };
            store.Set(accountId, session with
            {
                State = session.State with
                {
                    Enemies = session.State.Enemies.SetItem(0, enemy),
                },
            });

            var state = await client.GetFromJsonAsync<BattleStateDto>(
                "/api/v1/runs/current/battle");
            var intent = Assert.Single(
                state!.Enemies, actor => actor.InstanceId == enemy.InstanceId).Intent;

            Assert.NotNull(intent);
            Assert.Equal(expectedTotal, intent!.AttackAll);
            Assert.Equal(expectedHits, intent.AttackHits);
            Assert.Equal(expectedHits, intent.AttackAllHits);
            Assert.Null(intent.AttackSingle);
            Assert.Null(intent.AttackRandom);
        }
        finally
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task BattleStart_populates_adjustedUpgradedDescription_for_upgradable_cards()
    {
        var (client, _) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: "ctx-upgraded");
        try
        {
            var res = await client.PostAsync("/api/v1/runs/current/battle/start", null);
            res.EnsureSuccessStatusCode();
            var body = await res.Content.ReadFromJsonAsync<BattleActionResponseDto>();
            Assert.NotNull(body);

            var allCards = body!.State.Hand
                .Concat(body.State.DrawPile)
                .Concat(body.State.DiscardPile)
                .ToList();

            // 攻撃の基本は IsUpgradable なので AdjustedUpgradedDescription も非 null。
            var strike = allCards.FirstOrDefault(c => c.CardDefinitionId == "striker_basic_attack");
            Assert.NotNull(strike);
            Assert.NotNull(strike!.AdjustedUpgradedDescription);
            Assert.Equal("敵単体に[R:9|:自身の攻撃力×1.5]アタック。", strike.AdjustedUpgradedDescription);
        }
        finally
        {
            client.Dispose();
        }
    }
}
