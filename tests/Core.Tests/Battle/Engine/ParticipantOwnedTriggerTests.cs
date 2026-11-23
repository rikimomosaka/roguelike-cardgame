using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class ParticipantOwnedTriggerTests
{
    private const string CpuParticipantId = "cpu:wisp_inst";

    private static IRng Rng() =>
        new FakeRng(new int[100], Array.Empty<double>());

    [Fact]
    public void Cpu_play_fires_only_its_OnPlay_and_OnCombo_without_rewinding_resources()
    {
        var action = Card(
            "cpu_action", CardType.Attack, cost: 1,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 4));
        var primaryPower = Power(
            "primary_on_play",
            new CardEffect(
                "gainEnergy", EffectScope.Self, null, 100,
                Trigger: "OnPlayCard"));
        var cpuPower = Power(
            "cpu_play_combo",
            new CardEffect(
                "gainEnergy", EffectScope.Self, null, 3,
                Trigger: "OnPlayCard"),
            new CardEffect(
                "gainEnergy", EffectScope.Self, null, 5,
                ComboMin: 1, Trigger: "OnCombo"));
        var primaryRelic = Relic(
            "primary_play_relic", "OnPlayCard", 100);
        var cpuRelic = Relic(
            "cpu_play_relic", "OnPlayCard", 2);
        var state = MakeParticipantState(
            primaryEnergy: 10,
            secondaryEnergy: 2,
            secondaryHand: ImmutableArray.Create(
                BattleFixtures.MakeBattleCard(action.Id, "cpu-action-inst")),
            primaryRelics: [primaryRelic.Id],
            secondaryRelics: [cpuRelic.Id],
            primaryPowers: [PowerInstance(primaryPower)],
            secondaryPowers: [PowerInstance(cpuPower)]);
        var primaryBefore = state.Participants[0];
        var catalog = Catalog(
            cards: [action, primaryPower, cpuPower],
            relics: [primaryRelic, cpuRelic]);

        var (next, events) = BattleEngine.PlayCard(
            state, CpuParticipantId, handIndex: 0,
            targetEnemyIndex: 0, targetAllyIndex: 1,
            Rng(), catalog);

        var cpu = next.Participants[1];
        Assert.Equal(11, cpu.Energy); // 2 - 1 + 2 + 3 + 5
        Assert.Empty(cpu.Hand);
        Assert.Equal("cpu-action-inst", Assert.Single(cpu.DiscardPile).InstanceId);
        Assert.Equal(1, cpu.ComboCount);
        Assert.Equal(1, cpu.MaxComboThisTurn);
        Assert.Equal(primaryBefore, next.Participants[0]);
        Assert.Equal(primaryBefore.Energy, next.Energy);
        Assert.Equal(primaryBefore.Hand, next.Hand);
        Assert.DoesNotContain(events, battleEvent =>
            battleEvent.Note?.Contains("primary_", StringComparison.Ordinal) == true);

        var triggerEvents = events.Where(battleEvent =>
            battleEvent.Note?.Contains("cpu_play", StringComparison.Ordinal) == true
            || battleEvent.Note?.Contains("cpu_play_combo", StringComparison.Ordinal) == true)
            .ToArray();
        Assert.Equal(3, triggerEvents.Length);
        Assert.All(triggerEvents, battleEvent =>
        {
            Assert.Equal("wisp_inst", battleEvent.CasterInstanceId);
            Assert.Equal("wisp_inst", battleEvent.Source?.OwnerInstanceId);
            Assert.Equal("wisp_inst", battleEvent.Source?.ActorInstanceId);
        });
    }

    [Fact]
    public void OnHit_uses_attack_source_participants_relics_and_powers()
    {
        var primaryRelic = Relic("primary_hit_relic", "OnHit", 100);
        var cpuRelic = Relic("cpu_hit_relic", "OnHit", 2);
        var primaryPower = Power(
            "primary_hit_power",
            new CardEffect(
                "gainEnergy", EffectScope.Self, null, 100,
                Trigger: "OnHit"));
        var cpuPower = Power(
            "cpu_hit_power",
            new CardEffect(
                "gainEnergy", EffectScope.Self, null, 3,
                Trigger: "OnHit"));
        var source = new CombatSource(
            CombatSourceKind.CardEffect,
            OwnerSlot: 0,
            OwnerInstanceId: "hero_inst",
            ActorInstanceId: "wisp_inst",
            DefinitionId: "cpu_attack");
        var wisp = OwnedWisp() with
        {
            AttackSingle = AttackPool.Empty.Add(5, source),
        };
        var state = MakeParticipantState(
            primaryEnergy: 0,
            secondaryEnergy: 0,
            primaryRelics: [primaryRelic.Id],
            secondaryRelics: [cpuRelic.Id],
            primaryPowers: [PowerInstance(primaryPower)],
            secondaryPowers: [PowerInstance(cpuPower)],
            wisp: wisp) with
        {
            Phase = BattlePhase.PlayerAttacking,
        };
        var catalog = Catalog(
            cards: [primaryPower, cpuPower],
            relics: [primaryRelic, cpuRelic]);

        var (next, events) = PlayerAttackingResolver.Resolve(
            state, Rng(), catalog);

        Assert.Equal(0, next.Participants[0].Energy);
        Assert.Equal(5, next.Participants[1].Energy);
        Assert.Equal(15, next.Enemies[0].CurrentHp);
        Assert.DoesNotContain(events, battleEvent =>
            battleEvent.Note?.Contains("primary_hit", StringComparison.Ordinal) == true);
        var triggerEvents = events.Where(battleEvent =>
            battleEvent.Note?.Contains("cpu_hit", StringComparison.Ordinal) == true)
            .ToArray();
        Assert.Equal(2, triggerEvents.Length);
        Assert.All(triggerEvents, battleEvent =>
        {
            Assert.Equal("wisp_inst", battleEvent.CasterInstanceId);
            Assert.Equal("wisp_inst", battleEvent.Source?.OwnerInstanceId);
            Assert.Equal("wisp_inst", battleEvent.Source?.ActorInstanceId);
        });
    }

    [Fact]
    public void Enemy_all_attack_fires_each_damaged_participants_power_once()
    {
        var primaryPower = Power(
            "primary_damage_power",
            new CardEffect(
                "gainEnergy", EffectScope.Self, null, 2,
                Trigger: "OnDamageReceived"));
        var cpuPower = Power(
            "cpu_damage_power",
            new CardEffect(
                "gainEnergy", EffectScope.Self, null, 7,
                Trigger: "OnDamageReceived"));
        var state = MakeParticipantState(
            primaryEnergy: 0,
            secondaryEnergy: 0,
            primaryPowers: [PowerInstance(primaryPower)],
            secondaryPowers: [PowerInstance(cpuPower)],
            hero: OwnedHero(hp: 20),
            wisp: OwnedWisp(hp: 20)) with
        {
            Phase = BattlePhase.EnemyAttacking,
        };
        var catalog = Catalog(
            cards: [primaryPower, cpuPower],
            enemies: [BattleFixtures.GoblinDef(hp: 20, attack: 5)]);

        var (next, events) = EnemyAttackingResolver.Resolve(
            state, Rng(), catalog);

        Assert.Equal(15, next.Allies.Single(
            actor => actor.InstanceId == "hero_inst").CurrentHp);
        Assert.Equal(15, next.Allies.Single(
            actor => actor.InstanceId == "wisp_inst").CurrentHp);
        Assert.Equal(2, next.Participants[0].Energy);
        Assert.Equal(7, next.Participants[1].Energy);
        var triggerEvents = events.Where(battleEvent =>
            battleEvent.Note?.Contains("_damage_power", StringComparison.Ordinal) == true)
            .ToArray();
        Assert.Equal(2, triggerEvents.Length);
        Assert.Equal(
            ["hero_inst", "wisp_inst"],
            triggerEvents.Select(battleEvent => battleEvent.Source?.ActorInstanceId));
    }

    [Fact]
    public void TurnStart_fires_each_living_participants_relics_and_powers_in_stable_order()
    {
        var primaryRelic = Relic("primary_start_relic", "OnTurnStart", 1, "block");
        var cpuRelic = Relic("cpu_start_relic", "OnTurnStart", 10, "block");
        var primaryPower = Power(
            "primary_start_power",
            new CardEffect(
                "block", EffectScope.Self, null, 2,
                Trigger: "OnTurnStart"));
        var cpuPower = Power(
            "cpu_start_power",
            new CardEffect(
                "block", EffectScope.Self, null, 20,
                Trigger: "OnTurnStart"));
        var state = MakeParticipantState(
            primaryEnergy: 0,
            secondaryEnergy: 0,
            primaryRelics: [primaryRelic.Id],
            secondaryRelics: [cpuRelic.Id],
            primaryPowers: [PowerInstance(primaryPower)],
            secondaryPowers: [PowerInstance(cpuPower)]);
        var catalog = Catalog(
            cards: [primaryPower, cpuPower],
            relics: [primaryRelic, cpuRelic]);

        var (next, events) = TurnStartProcessor.Process(
            state, Rng(), catalog);

        Assert.Equal(3, next.Allies.Single(
            actor => actor.InstanceId == "hero_inst").Block.Sum);
        Assert.Equal(30, next.Allies.Single(
            actor => actor.InstanceId == "wisp_inst").Block.Sum);
        AssertTriggerOrder(
            events, "start_",
            (CombatSourceKind.RelicEffect, "hero_inst"),
            (CombatSourceKind.RelicEffect, "wisp_inst"),
            (CombatSourceKind.PowerEffect, "hero_inst"),
            (CombatSourceKind.PowerEffect, "wisp_inst"));
    }

    [Fact]
    public void All_participant_triggers_follow_actor_slot_order_after_a_slot_is_reused()
    {
        var slotOneRelic = Relic(
            "slot1_order_relic", "OnTurnStart", 1, "block");
        var slotTwoRelic = Relic(
            "slot2_order_relic", "OnTurnStart", 1, "block");
        var slotTwo = OwnedWisp() with
        {
            InstanceId = "slot2_actor",
            SlotIndex = 2,
        };
        var state = MakeParticipantState(
            primaryEnergy: 0,
            secondaryEnergy: 0,
            secondaryRelics: [slotTwoRelic.Id],
            wisp: slotTwo);
        var slotOne = BattleFixtures.SummonActor(
            "slot1_actor", "wisp", slotIndex: 1, hp: 10) with
        {
            OwnerPlayerSlot = 0,
            OwnerActorInstanceId = "hero_inst",
        };
        state = state with { Allies = state.Allies.Add(slotOne) };
        var slotOneParticipant = state.Participants[1] with
        {
            ParticipantId = "cpu:slot1_actor",
            ActorInstanceId = slotOne.InstanceId,
            TargetAllyIndex = 2,
            OwnedRelicIds = [slotOneRelic.Id],
        };
        // The reused slot-one participant is intentionally appended after the
        // existing slot-two participant.  Collection order must not become
        // turn/trigger order.
        state = BattleParticipantAccess.Add(state, slotOneParticipant);

        var (_, events) = RelicTriggerProcessor.FireForAllLivingParticipants(
            state,
            "OnTurnStart",
            Catalog(relics: [slotOneRelic, slotTwoRelic]),
            Rng(),
            orderStart: 0);

        AssertTriggerOrder(
            events,
            "_order_relic",
            (CombatSourceKind.RelicEffect, slotOne.InstanceId),
            (CombatSourceKind.RelicEffect, slotTwo.InstanceId));
    }

    [Fact]
    public void Secondary_base_draw_happens_before_its_OnTurnStart_power()
    {
        var costPower = Power(
            "cpu_start_cost_power",
            new CardEffect(
                "modifyCardCost", EffectScope.Self, null, -1,
                Pile: "hand", Select: "all", Trigger: "OnTurnStart",
                CostDuration: "untilTurnEnd"));
        var state = MakeParticipantState(
            primaryEnergy: 0,
            secondaryEnergy: 0,
            secondaryPowers: [PowerInstance(costPower)]);
        var secondary = state.Participants[1] with
        {
            DrawPerTurn = 1,
            DrawPile = [BattleFixtures.MakeBattleCard("defend", "cpu-drawn")],
        };
        state = BattleParticipantAccess.Replace(state, secondary);

        var (next, _) = TurnStartProcessor.Process(
            state, Rng(), Catalog(cards: [costPower]));

        var cpu = next.Participants[1];
        var drawn = Assert.Single(cpu.Hand);
        Assert.Equal("cpu-drawn", drawn.InstanceId);
        Assert.Equal(-1, Assert.Single(drawn.EffectiveCostModifiers).Amount);
    }

    [Fact]
    public void TurnEnd_fires_each_living_participants_relics_and_powers_in_stable_order()
    {
        var primaryRelic = Relic("primary_end_relic", "OnTurnEnd", 1, "block");
        var cpuRelic = Relic("cpu_end_relic", "OnTurnEnd", 10, "block");
        var primaryPower = Power(
            "primary_end_power",
            new CardEffect(
                "block", EffectScope.Self, null, 2,
                Trigger: "OnTurnEnd"));
        var cpuPower = Power(
            "cpu_end_power",
            new CardEffect(
                "block", EffectScope.Self, null, 20,
                Trigger: "OnTurnEnd"));
        var state = MakeParticipantState(
            primaryEnergy: 0,
            secondaryEnergy: 0,
            primaryRelics: [primaryRelic.Id],
            secondaryRelics: [cpuRelic.Id],
            primaryPowers: [PowerInstance(primaryPower)],
            secondaryPowers: [PowerInstance(cpuPower)]);
        var catalog = Catalog(
            cards: [primaryPower, cpuPower],
            relics: [primaryRelic, cpuRelic]);

        var (next, events) = TurnEndProcessor.Process(
            state, Rng(), catalog);

        Assert.Equal(3, next.Allies.Single(
            actor => actor.InstanceId == "hero_inst").Block.Sum);
        Assert.Equal(30, next.Allies.Single(
            actor => actor.InstanceId == "wisp_inst").Block.Sum);
        AssertTriggerOrder(
            events, "end_",
            (CombatSourceKind.RelicEffect, "hero_inst"),
            (CombatSourceKind.RelicEffect, "wisp_inst"),
            (CombatSourceKind.PowerEffect, "hero_inst"),
            (CombatSourceKind.PowerEffect, "wisp_inst"));
    }

    [Fact]
    public void EnemyDeath_fires_all_living_participant_owned_triggers_once()
    {
        var primaryRelic = Relic("primary_death_relic", "OnEnemyDeath", 1);
        var cpuRelic = Relic("cpu_death_relic", "OnEnemyDeath", 10);
        var primaryPower = Power(
            "primary_death_power",
            new CardEffect(
                "gainEnergy", EffectScope.Self, null, 2,
                Trigger: "OnEnemyDeath"));
        var cpuPower = Power(
            "cpu_death_power",
            new CardEffect(
                "gainEnergy", EffectScope.Self, null, 20,
                Trigger: "OnEnemyDeath"));
        var heroSource = new CombatSource(
            CombatSourceKind.CardEffect, 0, "hero_inst", "hero_inst",
            DefinitionId: "finisher");
        var hero = OwnedHero() with
        {
            AttackSingle = AttackPool.Empty.Add(100, heroSource),
        };
        var state = MakeParticipantState(
            primaryEnergy: 0,
            secondaryEnergy: 0,
            primaryRelics: [primaryRelic.Id],
            secondaryRelics: [cpuRelic.Id],
            primaryPowers: [PowerInstance(primaryPower)],
            secondaryPowers: [PowerInstance(cpuPower)],
            hero: hero) with
        {
            Phase = BattlePhase.PlayerAttacking,
        };
        var catalog = Catalog(
            cards: [primaryPower, cpuPower],
            relics: [primaryRelic, cpuRelic]);

        var (next, events) = PlayerAttackingResolver.Resolve(
            state, Rng(), catalog);

        Assert.Equal(3, next.Participants[0].Energy);
        Assert.Equal(30, next.Participants[1].Energy);
        Assert.Equal(next.Participants[0].Energy, next.Energy);
        AssertTriggerOrder(
            events, "death_",
            (CombatSourceKind.RelicEffect, "hero_inst"),
            (CombatSourceKind.RelicEffect, "wisp_inst"),
            (CombatSourceKind.PowerEffect, "hero_inst"),
            (CombatSourceKind.PowerEffect, "wisp_inst"));
        Assert.Equal(4, events.Count(battleEvent =>
            battleEvent.Note?.Contains("death_", StringComparison.Ordinal) == true));
    }

    private static void AssertTriggerOrder(
        IReadOnlyList<BattleEvent> events,
        string noteFragment,
        params (CombatSourceKind Kind, string ActorId)[] expected)
    {
        var actual = events
            .Where(battleEvent =>
                battleEvent.Note?.Contains(
                    noteFragment, StringComparison.Ordinal) == true)
            .Select(battleEvent => (
                battleEvent.Source!.Kind,
                battleEvent.Source.ActorInstanceId!))
            .ToArray();
        Assert.Equal(expected, actual);
    }

    private static BattleState MakeParticipantState(
        int primaryEnergy,
        int secondaryEnergy,
        ImmutableArray<BattleCardInstance> secondaryHand = default,
        ImmutableArray<string> primaryRelics = default,
        ImmutableArray<string> secondaryRelics = default,
        ImmutableArray<BattleCardInstance> primaryPowers = default,
        ImmutableArray<BattleCardInstance> secondaryPowers = default,
        CombatActor? hero = null,
        CombatActor? wisp = null)
    {
        hero ??= OwnedHero();
        wisp ??= OwnedWisp();
        var primaryHand = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("defend", "primary-defend"));
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero, wisp),
            hand: primaryHand,
            energy: primaryEnergy,
            ownedRelicIds: Normalize(primaryRelics)) with
        {
            PowerCards = Normalize(primaryPowers),
        };
        state = BattleParticipantAccess.InitializePrimary(state);
        var primary = state.Participants[0] with
        {
            OwnedRelicIds = Normalize(primaryRelics),
            PowerCards = Normalize(primaryPowers),
        };
        state = BattleParticipantAccess.Replace(state, primary);
        var secondary = primary with
        {
            ParticipantId = CpuParticipantId,
            ActorInstanceId = wisp.InstanceId,
            ControllerKind = BattleParticipantControllerKind.Cpu,
            OwnerPlayerSlot = 0,
            CharacterId = wisp.DefinitionId,
            TargetAllyIndex = 1,
            TargetEnemyIndex = 0,
            Energy = secondaryEnergy,
            EnergyPerTurn = 2,
            DrawPerTurn = 2,
            DrawPile = ImmutableArray<BattleCardInstance>.Empty,
            Hand = Normalize(secondaryHand),
            DiscardPile = ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile = ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld = ImmutableArray<BattleCardInstance>.Empty,
            PowerCards = Normalize(secondaryPowers),
            ComboCount = 0,
            MaxComboThisTurn = 0,
            ComboGauge = 0,
            LastPlayedOrigCost = null,
            NextCardComboFreePass = false,
            WildUsedInCurrentCombo = false,
            UsedCharacterSkillIdsThisTurn = ImmutableHashSet<string>.Empty,
            CharacterSkillUsesThisTurn = ImmutableDictionary<string, int>.Empty,
            CardUsesThisTurn = ImmutableDictionary<string, int>.Empty,
            CardUseLockedThisTurn = false,
            OwnedRelicIds = Normalize(secondaryRelics),
            Potions = ImmutableArray<string>.Empty,
            PotionUsesThisTurn = ImmutableDictionary<string, int>.Empty,
            PendingCardPlay = null,
            IsReady = false,
        };
        return BattleParticipantAccess.Add(state, secondary);
    }

    private static ImmutableArray<T> Normalize<T>(ImmutableArray<T> values) =>
        values.IsDefault ? ImmutableArray<T>.Empty : values;

    private static CombatActor OwnedHero(int hp = 70) =>
        BattleFixtures.Hero(hp: hp) with
        {
            OwnerPlayerSlot = 0,
            OwnerActorInstanceId = "hero_inst",
        };

    private static CombatActor OwnedWisp(int hp = 10) =>
        BattleFixtures.SummonActor(
            "wisp_inst", "wisp", slotIndex: 1, hp: hp) with
        {
            OwnerPlayerSlot = 0,
            OwnerActorInstanceId = "hero_inst",
        };

    private static CardDefinition Card(
        string id, CardType type, int cost, params CardEffect[] effects) =>
        new(
            id, id, null, CardRarity.Common, type,
            Cost: cost, UpgradedCost: null,
            Effects: effects, UpgradedEffects: null, Keywords: null);

    private static CardDefinition Power(
        string id, params CardEffect[] effects) =>
        Card(id, CardType.Power, cost: 1, effects);

    private static BattleCardInstance PowerInstance(CardDefinition definition) =>
        BattleFixtures.MakeBattleCard(
            definition.Id, $"{definition.Id}-instance");

    private static RelicDefinition Relic(
        string id, string trigger, int amount,
        string action = "gainEnergy") =>
        BattleFixtures.Relic(
            id, trigger, implemented: true,
            new CardEffect(action, EffectScope.Self, null, amount));

    private static DataCatalog Catalog(
        IEnumerable<CardDefinition>? cards = null,
        IEnumerable<RelicDefinition>? relics = null,
        IEnumerable<RoguelikeCardGame.Core.Battle.Definitions.EnemyDefinition>? enemies = null) =>
        BattleFixtures.MinimalCatalog(
            cards: (cards ?? Array.Empty<CardDefinition>())
                .Append(BattleFixtures.Defend()),
            relics: relics,
            enemies: enemies);
}
