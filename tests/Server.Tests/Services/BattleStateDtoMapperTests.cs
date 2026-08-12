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
                SourceId: "dto-test");
            var handCard = new BattleCardInstance(
                "dto-hand-cost", definition.Id, false, null,
                ImmutableArray.Create(modifier));
            var drawCard = new BattleCardInstance(
                "dto-draw-cost", definition.Id, false, null,
                ImmutableArray.Create(modifier));
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
                LastPlayedOrigCost = 4,
                NextCardComboFreePass = false,
            };

            var dto = BattleStateDtoMapper.ToDto(state, catalog);
            var projectedHand = Assert.Single(dto.Hand);
            var projectedDraw = Assert.Single(dto.DrawPile);

            Assert.Equal(5, projectedHand.OriginalCost);
            Assert.Equal(2, projectedHand.CurrentCost); // 5 - 1 + 1 - 2 - combo 1
            Assert.Equal(3, projectedHand.CostOverride); // legacy alias is pre-combo
            Assert.Equal(5, projectedDraw.OriginalCost);
            Assert.Equal(4, projectedDraw.CurrentCost); // only the per-card modifier applies
            Assert.Equal(4, projectedDraw.CostOverride);
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
            var state = session.State with
            {
                Allies = ImmutableArray.Create(firstHighSlotHero, secondLowSlotHero),
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

            // hero に buff 無し → strike (attack 6 single) は無修飾 [N:6] のまま。
            // 5 枚の strike + 5 枚の defend を含むデッキを想定。
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

            // strike (attack 6) の adjustedDescription は無修飾の [N:6]。
            var strike = allCards.FirstOrDefault(c => c.CardDefinitionId == "strike");
            if (strike is not null)
            {
                Assert.Equal("敵単体に[N:6]アタック。", strike.AdjustedDescription);
            }
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
            var strike = allCards.FirstOrDefault(c => c.CardDefinitionId == "strike");
            Assert.NotNull(strike);
            // 6 + 2 = 8、base 6 と異なるので up 修飾。
            // Phase 10.5.M6.4: 数字 marker 前後 space 全削除
            Assert.Equal("敵単体に[N:8|up]アタック。", strike!.AdjustedDescription);
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
            var strike = allCards.FirstOrDefault(c => c.CardDefinitionId == "strike");
            Assert.NotNull(strike);
            // 6 * 0.75 = 4.5 → floor 4、base 6 と異なるので down 修飾。
            // Phase 10.5.M6.4: 数字 marker 前後 space 全削除
            Assert.Equal("敵単体に[N:4|down]アタック。", strike!.AdjustedDescription);
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
            var defend = allCards.FirstOrDefault(c => c.CardDefinitionId == "defend");
            Assert.NotNull(defend);
            // defend は block scope=Single side=Ally なので "味方 1 体にブロック ..." 文言。
            // dexterity=3 で 5 + 3 = 8、base 5 と異なるので up 修飾。
            Assert.Contains("|up]", defend!.AdjustedDescription);
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
            store.Set(accountId, session with
            {
                State = session.State with
                {
                    Allies = session.State.Allies.SetItem(0, hero),
                    Enemies = session.State.Enemies.SetItem(0, enemy),
                    TargetEnemyIndex = 0,
                },
            });

            var response = await client.PostAsync(
                "/api/v1/runs/current/battle/end-turn", null);
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
    public async Task Summon_intent_preserves_move_attack_scope()
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
                CurrentMoveId = "wisp_strike",
                AttackSingle = AttackPool.Empty,
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
    [InlineData("goblin_c", "throw_knife", 8, 2)]
    [InlineData("six_ghost", "divider", 36, 6)]
    public async Task Enemy_multihit_intent_reports_scope_total_and_real_hit_count(
        string definitionId, string moveId, int expectedTotal, int expectedHits)
    {
        var (client, accountId) = await BattleControllerFixtures.SetupRunWithActiveBattleAsync(
            _factory, accountId: $"intent-enemy-{definitionId}");
        try
        {
            await client.PostAsync("/api/v1/runs/current/battle/start", null);
            var store = _factory.Services.GetRequiredService<BattleSessionStore>();
            Assert.True(store.TryGet(accountId, out var session));
            var enemy = session.State.Enemies[0] with
            {
                DefinitionId = definitionId,
                CurrentMoveId = moveId,
                Statuses = ImmutableDictionary<string, int>.Empty,
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

            // strike は IsUpgradable なので AdjustedUpgradedDescription も非 null。
            var strike = allCards.FirstOrDefault(c => c.CardDefinitionId == "strike");
            Assert.NotNull(strike);
            Assert.NotNull(strike!.AdjustedUpgradedDescription);
            Assert.Equal("敵単体に[N:9]アタック。", strike.AdjustedUpgradedDescription);
        }
        finally
        {
            client.Dispose();
        }
    }
}
