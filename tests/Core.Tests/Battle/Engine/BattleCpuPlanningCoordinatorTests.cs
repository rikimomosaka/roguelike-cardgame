using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public sealed class BattleCpuPlanningCoordinatorTests
{
    [Fact]
    public void EnsureParticipants_initializes_wisp_resources_once_without_touching_primary_mirror()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        var hero = BattleFixtures.Hero();
        var wisp = BattleFixtures.SummonActor(
            "wisp-instance", "wisp", slotIndex: 1, hp: 30, lifetime: 9,
            moveId: "legacy-move") with
        {
            OwnerPlayerSlot = 0,
            OwnerActorInstanceId = hero.InstanceId,
        };
        var heroCard = new BattleCardInstance("hero-card", "strike", false, null);
        var state = BattleFixtures.MinimalState(
            allies: [hero, wisp],
            energy: 7,
            hand: [heroCard]);
        state = BattleParticipantAccess.InitializePrimary(state);
        var primaryBefore = BattleParticipantAccess.GetPrimary(state);

        var initialized = BattleCpuPlanningCoordinator.EnsureParticipants(
            state, new MinimumRng(), catalog);
        var initializedAgain = BattleCpuPlanningCoordinator.EnsureParticipants(
            initialized, new MinimumRng(), catalog);

        var cpu = Assert.Single(initialized.Participants.Where(
            participant => participant.ControllerKind == BattleParticipantControllerKind.Cpu));
        Assert.Equal("cpu:wisp-instance", cpu.ParticipantId);
        Assert.Equal(1, cpu.Energy);
        Assert.Equal(1, cpu.EnergyPerTurn);
        Assert.Equal(3, cpu.DrawPerTurn);
        Assert.Equal(3, cpu.Hand.Length);
        Assert.Equal(3, cpu.DrawPile.Length);
        Assert.Equal(6, cpu.Hand.Length + cpu.DrawPile.Length);
        Assert.Empty(cpu.DiscardPile);
        Assert.Empty(cpu.ExhaustPile);
        Assert.Empty(cpu.OwnedRelicIds);
        Assert.Empty(cpu.Potions);
        Assert.False(cpu.IsReady);
        Assert.Equal(primaryBefore, BattleParticipantAccess.GetPrimary(initialized));
        Assert.Equal(primaryBefore.Energy, initialized.Energy);
        Assert.Equal(primaryBefore.Hand, initialized.Hand);
        Assert.Equal(2, initializedAgain.Participants.Length);
        Assert.Equal(cpu, initializedAgain.Participants[1]);
    }

    [Fact]
    public void EnsureParticipants_initial_draw_uses_the_summons_confusion()
    {
        var card = Card("confused-opening", CardType.Skill, cost: 2);
        var unit = CardUnit(
            "confused-unit",
            [new UnitDeckEntry(card.Id)],
            drawPerTurn: 1,
            handLimit: 1);
        var actor = OwnedSummon(
            "confused-instance", unit.Id, slotIndex: 1) with
        {
            Statuses = ImmutableDictionary<string, int>.Empty
                .Add(BattleStatusIds.Confusion, 1),
        };
        var state = BattleParticipantAccess.InitializePrimary(
            BattleFixtures.MinimalState(
                allies: [BattleFixtures.Hero(), actor]));
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [card], units: [unit]);

        var initialized = BattleCpuPlanningCoordinator.EnsureParticipants(
            state, new MinimumRng(), catalog);

        var cpu = initialized.Participants.Single(participant =>
            participant.ActorInstanceId == actor.InstanceId);
        var openingCard = Assert.Single(cpu.Hand);
        Assert.Equal(0, openingCard.CostOverride);
        Assert.Empty(openingCard.EffectiveCostModifiers);
    }

    [Fact]
    public void EnsureParticipants_copies_unit_loadout_and_shared_skill_and_potion_commands_use_it()
    {
        var filler = Card("loadout-filler", CardType.Skill, cost: 9);
        var skill = new CharacterSkillDefinition(
            "kindle", "火勢", new ResourceCost(Energy: 0, ComboGauge: 0),
            OncePerTurn: false,
            Effects: [new CardEffect("gainEnergy", EffectScope.Self, null, 2)],
            Keywords: ["wild"],
            UsesPerTurn: 1);
        var relic = BattleFixtures.Relic("cpu-relic", "OnTurnStart", true,
            new CardEffect("buff", EffectScope.Self, null, 1, Name: "strength"));
        var potion = BattleFixtures.Potion("cpu-potion",
            new CardEffect("buff", EffectScope.Self, null, 1, Name: "dexterity"));
        var unit = CardUnit(
            "loadout-unit",
            [new UnitDeckEntry(filler.Id)],
            initialEnergy: 0,
            drawPerTurn: 1,
            handLimit: 1,
            skills: [skill],
            ownedRelicIds: [relic.Id],
            potions: [potion.Id]);
        var actor = OwnedSummon("loadout-instance", unit.Id, slotIndex: 1);
        var state = BattleFixtures.MinimalState(
            allies: [BattleFixtures.Hero(), actor]);
        state = BattleParticipantAccess.InitializePrimary(state);
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [filler], units: [unit], relics: [relic], potions: [potion]);

        var initialized = BattleCpuPlanningCoordinator.EnsureParticipants(
            state, new MinimumRng(), catalog);
        var participant = initialized.Participants.Single(candidate =>
            candidate.ParticipantId == "cpu:loadout-instance");

        Assert.Equal(relic.Id, Assert.Single(participant.OwnedRelicIds));
        Assert.Equal(potion.Id, Assert.Single(participant.Potions));

        var (afterSkill, _) = BattleEngine.UseCharacterSkill(
            initialized, participant.ParticipantId, skill.Id, catalog, new MinimumRng());
        var skillOwner = afterSkill.Participants.Single(candidate =>
            candidate.ParticipantId == participant.ParticipantId);
        Assert.Equal(2, skillOwner.Energy);
        Assert.Equal(1, skillOwner.CharacterSkillUsesThisTurn[skill.Id]);

        var (afterPotion, _) = BattleEngine.UsePotion(
            afterSkill, participant.ParticipantId, potionIndex: 0,
            targetEnemyIndex: null, targetAllyIndex: null,
            new MinimumRng(), catalog);
        var potionOwner = afterPotion.Participants.Single(candidate =>
            candidate.ParticipantId == participant.ParticipantId);
        Assert.Equal(string.Empty, Assert.Single(potionOwner.Potions));
        Assert.Equal(1, afterPotion.Allies.Single(candidate =>
            candidate.InstanceId == actor.InstanceId).GetStatus("dexterity"));
    }

    [Fact]
    public void Card_controlled_summon_has_no_move_or_lifetime_and_can_be_initialized()
    {
        var summonCard = Card(
            "summon-wisp", CardType.Unit, cost: 0,
            new CardEffect("summon", EffectScope.Self, null, 0, UnitId: "wisp"));
        var wispCard = Card(
            "wisp-hit", CardType.Attack, cost: 0,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 1));
        var wisp = CardUnit(
            "wisp", [new UnitDeckEntry("wisp-hit")],
            lifetime: 12,
            initialMoveId: "should-not-be-used");
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [summonCard, wispCard], units: [wisp]);
        var state = BattleFixtures.MinimalState(
            hand: [new BattleCardInstance("summon-card-instance", summonCard.Id, false, null)],
            energy: 1);
        state = BattleParticipantAccess.InitializePrimary(state);

        var (summoned, _) = BattleEngine.PlayCard(
            state,
            BattleParticipantAccess.DefaultPrimaryParticipantId,
            handIndex: 0,
            targetEnemyIndex: 0,
            targetAllyIndex: 0,
            new MinimumRng(),
            catalog);
        var summon = Assert.Single(summoned.Allies.Where(actor => actor.DefinitionId == "wisp"));

        Assert.Null(summon.CurrentMoveId);
        Assert.Null(summon.RemainingLifetimeTurns);
        var initialized = BattleCpuPlanningCoordinator.EnsureParticipants(
            summoned, new MinimumRng(), catalog);
        Assert.True(BattleParticipantAccess.TryGetByActor(
            initialized, summon.InstanceId, out var participant));
        Assert.Equal(BattleParticipantControllerKind.Cpu, participant.ControllerKind);
    }

    [Fact]
    public void PlanUntilReady_recomputes_greedy_choice_after_each_play_and_preserves_primary_resources()
    {
        var free = Card(
            "free", CardType.Attack, cost: 0,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 1));
        var one = Card(
            "one", CardType.Attack, cost: 1,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 2));
        var unit = CardUnit(
            "planner",
            [new UnitDeckEntry("one"), new UnitDeckEntry("free")],
            initialEnergy: 1,
            drawPerTurn: 2,
            handLimit: 2);
        var actor = OwnedSummon("planner-instance", "planner", slotIndex: 1);
        var state = BattleFixtures.MinimalState(allies: [BattleFixtures.Hero(), actor]);
        state = BattleParticipantAccess.InitializePrimary(state);
        var primaryBefore = BattleParticipantAccess.GetPrimary(state);
        var catalog = BattleFixtures.MinimalCatalog(cards: [free, one], units: [unit]);

        var result = BattleCpuPlanningCoordinator.PlanUntilReady(
            state, new MinimumRng(), catalog);

        Assert.Equal(2, result.Actions.Length);
        Assert.Equal("free", result.Actions[0].CardDefinitionId);
        Assert.Equal("one", result.Actions[1].CardDefinitionId);
        Assert.All(result.Actions, action => Assert.Equal("cpu:planner-instance", action.ParticipantId));
        var cpu = result.State.Participants.Single(p => p.ParticipantId == "cpu:planner-instance");
        Assert.True(cpu.IsReady);
        Assert.Empty(cpu.Hand);
        // Playing the original-cost 0 card first makes the original-cost 1
        // card a discounted combo continuation, so its payable cost is zero.
        Assert.Equal(1, cpu.Energy);
        Assert.Equal(0, result.Actions[1].PaidEnergy);
        var firstAction = result.Actions[0];
        Assert.Equal("free", firstAction.CardSnapshot?.CardDefinitionId);
        Assert.Equal(0, firstAction.CostBreakdown?.OriginalCost);
        Assert.Equal(0, firstAction.CostBreakdown?.PayableCost);
        Assert.Equal(0, firstAction.UsesBefore);
        Assert.True(firstAction.RevisionAfter > firstAction.RevisionBefore);
        Assert.Equal(
            state.Enemies[0].InstanceId,
            Assert.Single(firstAction.TargetActorInstanceIds));
        Assert.Equal(
            state.Enemies[0].InstanceId,
            Assert.Single(firstAction.IntentDelta.AddedSingleHits).TargetInstanceId);
        Assert.Empty(firstAction.IntentDelta.AddedRandomHits);
        Assert.Empty(firstAction.IntentDelta.AddedAllHits);
        Assert.Contains(result.Stops, stop =>
            stop.ParticipantId == cpu.ParticipantId
            && stop.Reason == CpuPlanningStopReason.NoLegalAction);
        Assert.Equal(primaryBefore, BattleParticipantAccess.GetPrimary(result.State));
        Assert.Equal(primaryBefore.Energy, result.State.Energy);
        Assert.Equal(primaryBefore.Hand, result.State.Hand);
    }

    [Fact]
    public void PlanUntilReady_uses_actor_slot_order_even_when_participants_are_stored_in_reverse()
    {
        var firstCard = Card(
            "slot-first-card", CardType.Attack, cost: 0,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 1));
        var secondCard = Card(
            "slot-second-card", CardType.Attack, cost: 0,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 1));
        var firstUnit = CardUnit(
            "slot-first-unit", [new UnitDeckEntry(firstCard.Id)],
            drawPerTurn: 1, handLimit: 1);
        var secondUnit = CardUnit(
            "slot-second-unit", [new UnitDeckEntry(secondCard.Id)],
            drawPerTurn: 1, handLimit: 1);
        var firstActor = OwnedSummon("slot-first", firstUnit.Id, slotIndex: 1);
        var secondActor = OwnedSummon("slot-second", secondUnit.Id, slotIndex: 2);
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [firstCard, secondCard], units: [firstUnit, secondUnit]);
        var state = BattleFixtures.MinimalState(
            allies: [BattleFixtures.Hero(), firstActor, secondActor]);
        state = BattleParticipantAccess.InitializePrimary(state);
        state = BattleCpuPlanningCoordinator.EnsureParticipants(
            state, new MinimumRng(), catalog);
        state = state with
        {
            Participants =
            [state.Participants[0], state.Participants[2], state.Participants[1]],
        };

        var result = BattleCpuPlanningCoordinator.PlanUntilReady(
            state, new MinimumRng(), catalog);

        Assert.Equal(
            ["cpu:slot-first", "cpu:slot-second"],
            result.Actions.Select(action => action.ParticipantId));
    }

    [Fact]
    public void Planning_ordinals_interleave_noop_ready_and_card_actions_in_commit_order()
    {
        var unavailableCard = Card("slot-one-expensive", CardType.Skill, cost: 9);
        var playableCard = Card(
            "slot-two-free", CardType.Attack, cost: 0,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 2));
        var firstUnit = CardUnit(
            "slot-one-noop", [new UnitDeckEntry(unavailableCard.Id)],
            initialEnergy: 0, drawPerTurn: 1, handLimit: 1);
        var secondUnit = CardUnit(
            "slot-two-action", [new UnitDeckEntry(playableCard.Id)],
            initialEnergy: 0, drawPerTurn: 1, handLimit: 1);
        var firstActor = OwnedSummon("slot-one-instance", firstUnit.Id, 1);
        var secondActor = OwnedSummon("slot-two-instance", secondUnit.Id, 2);
        var state = BattleFixtures.MinimalState(
            allies: [BattleFixtures.Hero(), firstActor, secondActor]);
        state = BattleParticipantAccess.InitializePrimary(state);
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [unavailableCard, playableCard],
            units: [firstUnit, secondUnit]);

        var result = BattleCpuPlanningCoordinator.PlanUntilReady(
            state, new MinimumRng(), catalog);

        var firstStop = Assert.Single(result.Stops.Where(stop =>
            stop.ParticipantId == "cpu:slot-one-instance"));
        var action = Assert.Single(result.Actions);
        var secondStop = Assert.Single(result.Stops.Where(stop =>
            stop.ParticipantId == "cpu:slot-two-instance"));
        Assert.Equal(0, firstStop.PlanningOrdinal);
        Assert.Equal(1, action.PlanningOrdinal);
        Assert.Equal(2, secondStop.PlanningOrdinal);
        Assert.Same(result.StateBeforePlanning, firstStop.StateBefore);
        Assert.Same(firstStop.StateAfter, action.StateBefore);
        Assert.Same(action.StateAfter, secondStop.StateBefore);
        Assert.Same(secondStop.StateAfter, result.State);
    }

    [Fact]
    public void External_energy_gain_reopens_ready_cpu_appends_action_and_readies_it_again()
    {
        var one = Card(
            "external-energy", CardType.Attack, cost: 1,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 2));
        var unit = CardUnit(
            "external-energy-unit", [new UnitDeckEntry("external-energy")],
            initialEnergy: 0, drawPerTurn: 1, handLimit: 1);
        var actor = OwnedSummon(
            "external-energy-instance", "external-energy-unit", 1);
        var state = BattleFixtures.MinimalState(
            allies: [BattleFixtures.Hero(), actor]);
        state = BattleParticipantAccess.InitializePrimary(state);
        var catalog = BattleFixtures.MinimalCatalog(cards: [one], units: [unit]);
        var initiallyPlanned = BattleCpuPlanningCoordinator.PlanUntilReady(
            state, new MinimumRng(), catalog);
        var readyCpu = initiallyPlanned.State.Participants.Single(
            p => p.ParticipantId == "cpu:external-energy-instance");
        Assert.True(readyCpu.IsReady);
        Assert.Empty(initiallyPlanned.Actions);

        var externallyChanged = BattleParticipantAccess.Replace(
            initiallyPlanned.State, readyCpu with { Energy = 1 });
        var replanned = BattleCpuPlanningCoordinator.PlanAfterExternalStateChange(
            externallyChanged, new MinimumRng(), catalog);

        var action = Assert.Single(replanned.Actions);
        Assert.Equal("external-energy", action.CardDefinitionId);
        Assert.Contains(readyCpu.ParticipantId, replanned.ReopenedParticipantIds);
        var finalCpu = replanned.State.Participants.Single(
            p => p.ParticipantId == readyCpu.ParticipantId);
        Assert.True(finalCpu.IsReady);
        Assert.Empty(finalCpu.Hand);
        Assert.Equal(0, finalCpu.Energy);
    }

    [Fact]
    public void External_numeric_change_does_not_reopen_cpu_without_a_new_legal_card()
    {
        var expensive = Card("still-expensive", CardType.Attack, cost: 3);
        var unit = CardUnit(
            "numeric-unit", [new UnitDeckEntry("still-expensive")],
            initialEnergy: 0, drawPerTurn: 1, handLimit: 1);
        var actor = OwnedSummon("numeric-instance", "numeric-unit", 1);
        var state = BattleFixtures.MinimalState(
            allies: [BattleFixtures.Hero(), actor]);
        state = BattleParticipantAccess.InitializePrimary(state);
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [expensive], units: [unit]);
        var initiallyPlanned = BattleCpuPlanningCoordinator.PlanUntilReady(
            state, new MinimumRng(), catalog);
        var changed = initiallyPlanned.State with
        {
            Enemies = initiallyPlanned.State.Enemies.SetItem(
                0,
                initiallyPlanned.State.Enemies[0] with
                {
                    Block = BlockPool.Empty.Add(4),
                }),
        };

        var replanned = BattleCpuPlanningCoordinator.PlanAfterExternalStateChange(
            changed, new MinimumRng(), catalog);

        Assert.Empty(replanned.Actions);
        Assert.Empty(replanned.ReopenedParticipantIds);
        Assert.True(replanned.State.Participants.Single(
            p => p.ParticipantId == "cpu:numeric-instance").IsReady);
    }

    [Fact]
    public void Cpu_action_reopens_ready_human_at_that_action_boundary()
    {
        var humanCard = Card("human-cost-one", CardType.Skill, cost: 1);
        var cpuGrant = Card(
            "cpu-grant-all-energy", CardType.Skill, cost: 0,
            new CardEffect("gainEnergy", EffectScope.All, EffectSide.Ally, 1));
        var unit = CardUnit(
            "cpu-support-unit", [new UnitDeckEntry(cpuGrant.Id)],
            initialEnergy: 0, drawPerTurn: 1, handLimit: 1);
        var cpuActor = OwnedSummon("cpu-support-instance", unit.Id, 1);
        var state = BattleFixtures.MinimalState(
            allies: [BattleFixtures.Hero(), cpuActor],
            energy: 0,
            hand: [new BattleCardInstance(
                "human-card-instance", humanCard.Id, false, null)]);
        state = BattleParticipantAccess.InitializePrimary(state);
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [humanCard, cpuGrant], units: [unit]);
        state = BattleCpuPlanningCoordinator.EnsureParticipants(
            state, new MinimumRng(), catalog);
        var primary = BattleParticipantAccess.GetPrimary(state);
        state = BattleParticipantAccess.Replace(
            state, primary with { IsReady = true, Energy = 0 });

        var planned = BattleCpuPlanningCoordinator.PlanUntilReady(
            state, new MinimumRng(), catalog);

        var action = Assert.Single(planned.Actions);
        Assert.Contains(primary.ParticipantId, action.ReopenedParticipantIds);
        Assert.True(BattleParticipantAccess.GetPrimary(action.StateBefore!).IsReady);
        Assert.False(BattleParticipantAccess.GetPrimary(action.StateAfter!).IsReady);
        Assert.Equal(1, BattleParticipantAccess.GetPrimary(action.StateAfter!).Energy);
        Assert.Contains(primary.ParticipantId, planned.ReopenedParticipantIds);
        Assert.False(BattleParticipantAccess.GetPrimary(planned.State).IsReady);
    }

    [Fact]
    public void Unit_skill_is_part_of_participant_legal_action_recheck()
    {
        var filler = Card("skill-candidate-filler", CardType.Skill, cost: 9);
        var skill = new CharacterSkillDefinition(
            "unit-skill", "術", new ResourceCost(Energy: 1),
            OncePerTurn: false,
            Effects: [new CardEffect("block", EffectScope.Self, null, 1)]);
        var unit = CardUnit(
            "skill-candidate-unit", [new UnitDeckEntry(filler.Id)],
            initialEnergy: 0, drawPerTurn: 1, handLimit: 1,
            skills: [skill]);
        var actor = OwnedSummon("skill-candidate-instance", unit.Id, 1);
        var state = BattleFixtures.MinimalState(
            allies: [BattleFixtures.Hero(), actor]);
        state = BattleParticipantAccess.InitializePrimary(state);
        var catalog = BattleFixtures.MinimalCatalog(cards: [filler], units: [unit]);
        var initialized = BattleCpuPlanningCoordinator.EnsureParticipants(
            state, new MinimumRng(), catalog);
        var secondary = initialized.Participants.Single(candidate =>
            candidate.ActorInstanceId == actor.InstanceId) with
        {
            ControllerKind = BattleParticipantControllerKind.Human,
            IsReady = true,
            Energy = 0,
        };
        var before = BattleParticipantAccess.Replace(initialized, secondary);
        var after = BattleParticipantAccess.Replace(
            before, secondary with { Energy = 1 });

        var checkedResult = BattleCpuPlanningCoordinator.PlanAfterCommittedAction(
            before,
            after,
            BattleParticipantAccess.DefaultPrimaryParticipantId,
            new MinimumRng(),
            catalog);

        Assert.Contains(secondary.ParticipantId, checkedResult.ReopenedParticipantIds);
        Assert.False(checkedResult.State.Participants.Single(candidate =>
            candidate.ParticipantId == secondary.ParticipantId).IsReady);
    }

    [Fact]
    public void Unit_potion_is_part_of_participant_legal_action_recheck()
    {
        var filler = Card("potion-candidate-filler", CardType.Skill, cost: 9);
        var potion = BattleFixtures.Potion(
            "unit-potion",
            new CardEffect("block", EffectScope.Self, null, 1)) with
        {
            UsesPerTurn = 1,
        };
        var unit = CardUnit(
            "potion-candidate-unit", [new UnitDeckEntry(filler.Id)],
            initialEnergy: 0, drawPerTurn: 1, handLimit: 1,
            potions: [potion.Id]);
        var actor = OwnedSummon("potion-candidate-instance", unit.Id, 1);
        var state = BattleFixtures.MinimalState(
            allies: [BattleFixtures.Hero(), actor]);
        state = BattleParticipantAccess.InitializePrimary(state);
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [filler], units: [unit], potions: [potion]);
        var initialized = BattleCpuPlanningCoordinator.EnsureParticipants(
            state, new MinimumRng(), catalog);
        var secondary = initialized.Participants.Single(candidate =>
            candidate.ActorInstanceId == actor.InstanceId) with
        {
            ControllerKind = BattleParticipantControllerKind.Human,
            IsReady = true,
            PotionUsesThisTurn = ImmutableDictionary<string, int>.Empty
                .Add(potion.Id, 1),
        };
        var before = BattleParticipantAccess.Replace(initialized, secondary);
        var after = BattleParticipantAccess.Replace(
            before,
            secondary with
            {
                PotionUsesThisTurn = ImmutableDictionary<string, int>.Empty,
            });

        var checkedResult = BattleCpuPlanningCoordinator.PlanAfterCommittedAction(
            before,
            after,
            BattleParticipantAccess.DefaultPrimaryParticipantId,
            new MinimumRng(),
            catalog);

        Assert.Contains(secondary.ParticipantId, checkedResult.ReopenedParticipantIds);
        Assert.False(checkedResult.State.Participants.Single(candidate =>
            candidate.ParticipantId == secondary.ParticipantId).IsReady);
    }

    [Fact]
    public void PlanUntilReady_discovers_and_plans_nested_cpu_immediately()
    {
        var callChild = Card(
            "call-child", CardType.Unit, cost: 0,
            new CardEffect("summon", EffectScope.Self, null, 0, UnitId: "child"));
        var childHit = Card(
            "child-hit", CardType.Attack, cost: 0,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 4));
        var parentUnit = CardUnit(
            "parent", [new UnitDeckEntry("call-child")],
            drawPerTurn: 1, handLimit: 1);
        var childUnit = CardUnit(
            "child", [new UnitDeckEntry("child-hit")],
            drawPerTurn: 1, handLimit: 1);
        var parent = OwnedSummon("parent-instance", "parent", slotIndex: 1);
        var state = BattleFixtures.MinimalState(
            allies: [BattleFixtures.Hero(), parent]);
        state = BattleParticipantAccess.InitializePrimary(state);
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [callChild, childHit],
            units: [parentUnit, childUnit]);

        var result = BattleCpuPlanningCoordinator.PlanUntilReady(
            state, new MinimumRng(), catalog);

        Assert.Equal(2, result.Actions.Length);
        Assert.Equal("call-child", result.Actions[0].CardDefinitionId);
        Assert.Equal("child-hit", result.Actions[1].CardDefinitionId);
        Assert.Equal("cpu:parent-instance", result.Actions[0].ParticipantId);
        Assert.StartsWith("cpu:summon_inst_", result.Actions[1].ParticipantId);
        Assert.Equal(2, result.InitializedParticipantIds.Length);
        var parentAction = result.Actions[0];
        var childAction = result.Actions[1];
        var childStop = Assert.Single(result.Stops.Where(stop =>
            stop.ParticipantId == childAction.ParticipantId));
        var parentStop = Assert.Single(result.Stops.Where(stop =>
            stop.ParticipantId == parentAction.ParticipantId));
        Assert.Equal(0, parentAction.PlanningOrdinal);
        Assert.Equal(1, childAction.PlanningOrdinal);
        Assert.Equal(2, childStop.PlanningOrdinal);
        Assert.Equal(3, parentStop.PlanningOrdinal);
        Assert.True(BattleParticipantAccess.TryGet(
            parentAction.StateAfter!, childAction.ParticipantId, out _));
        Assert.Same(parentAction.StateAfter, childAction.StateBefore);
        Assert.Same(childAction.StateAfter, childStop.StateBefore);
        Assert.Same(childStop.StateAfter, parentStop.StateBefore);
        Assert.Same(parentStop.StateAfter, result.State);
        Assert.All(
            result.State.Participants.Where(p => p.ControllerKind == BattleParticipantControllerKind.Cpu),
            participant => Assert.True(participant.IsReady));
    }

    [Fact]
    public void PlanUntilReady_resolves_pending_choice_with_first_candidates()
    {
        var chooser = Card(
            "chooser", CardType.Skill, cost: 0,
            new CardEffect(
                "discard", EffectScope.Self, null, 1,
                Pile: "hand", Select: "choose"));
        var filler = Card("filler", CardType.Skill, cost: 9);
        var unit = CardUnit(
            "chooser-unit",
            [new UnitDeckEntry("chooser"), new UnitDeckEntry("filler", Count: 2)],
            drawPerTurn: 3,
            handLimit: 3);
        var actor = OwnedSummon("chooser-instance", "chooser-unit", 1);
        var state = BattleFixtures.MinimalState(allies: [BattleFixtures.Hero(), actor]);
        state = BattleParticipantAccess.InitializePrimary(state);
        var catalog = BattleFixtures.MinimalCatalog(cards: [chooser, filler], units: [unit]);

        var initialized = BattleCpuPlanningCoordinator.EnsureParticipants(
            state, new MinimumRng(), catalog);
        var before = initialized.Participants.Single(p => p.ParticipantId == "cpu:chooser-instance");
        var firstCandidate = before.Hand
            .Where(card => card.CardDefinitionId == "filler")
            .Select(card => card.InstanceId)
            .First();
        var result = BattleCpuPlanningCoordinator.PlanUntilReady(
            initialized, new MinimumRng(), catalog);
        var after = result.State.Participants.Single(p => p.ParticipantId == "cpu:chooser-instance");

        var action = Assert.Single(result.Actions);
        Assert.Equal(firstCandidate, Assert.Single(action.SelectedInstanceIds));
        Assert.Null(after.PendingCardPlay);
        Assert.Contains(after.DiscardPile, card => card.InstanceId == firstCandidate);
    }

    [Fact]
    public void PlanUntilReady_stops_repeated_effective_state_and_marks_ready()
    {
        var repeat = Card(
            "repeat", CardType.Skill, cost: 0,
            new CardEffect("retainSelf", EffectScope.Self, null, 0));
        var unit = CardUnit(
            "repeat-unit", [new UnitDeckEntry("repeat")],
            drawPerTurn: 1, handLimit: 1);
        var actor = OwnedSummon("repeat-instance", "repeat-unit", 1);
        var state = BattleFixtures.MinimalState(allies: [BattleFixtures.Hero(), actor]);
        state = BattleParticipantAccess.InitializePrimary(state);
        var catalog = BattleFixtures.MinimalCatalog(cards: [repeat], units: [unit]);

        var result = BattleCpuPlanningCoordinator.PlanUntilReady(
            state, new MinimumRng(), catalog);

        var stop = Assert.Single(result.Stops.Where(
            candidate => candidate.ParticipantId == "cpu:repeat-instance"));
        Assert.Equal(CpuPlanningStopReason.RepeatedState, stop.Reason);
        Assert.InRange(stop.PlaysCommitted, 2, BattleCpuPlanningCoordinator.MaxPlaysPerActor - 1);
        Assert.True(result.State.Participants.Single(
            p => p.ParticipantId == "cpu:repeat-instance").IsReady);
    }

    [Fact]
    public void PlanUntilReady_enforces_per_actor_play_cap()
    {
        var repeatAttack = Card(
            "repeat-attack", CardType.Attack, cost: 0,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 1),
            new CardEffect("retainSelf", EffectScope.Self, null, 0));
        var unit = CardUnit(
            "cap-unit", [new UnitDeckEntry("repeat-attack")],
            drawPerTurn: 1, handLimit: 1);
        var actor = OwnedSummon("cap-instance", "cap-unit", 1);
        var state = BattleFixtures.MinimalState(allies: [BattleFixtures.Hero(), actor]);
        state = BattleParticipantAccess.InitializePrimary(state);
        var catalog = BattleFixtures.MinimalCatalog(cards: [repeatAttack], units: [unit]);

        var result = BattleCpuPlanningCoordinator.PlanUntilReady(
            state, new MinimumRng(), catalog);

        Assert.Equal(BattleCpuPlanningCoordinator.MaxPlaysPerActor, result.Actions.Length);
        Assert.Contains(result.Stops, stop =>
            stop.ParticipantId == "cpu:cap-instance"
            && stop.Reason == CpuPlanningStopReason.PerActorPlayCap
            && stop.PlaysCommitted == BattleCpuPlanningCoordinator.MaxPlaysPerActor);
        Assert.True(result.State.Participants.Single(
            p => p.ParticipantId == "cpu:cap-instance").IsReady);
        Assert.Equal(
            BattleCpuPlanningCoordinator.MaxPlaysPerActor,
            result.State.Participants.Single(
                p => p.ParticipantId == "cpu:cap-instance")
                .CpuCardPlaysThisTurn);
    }

    [Fact]
    public void Per_turn_play_cap_survives_reopened_planning_calls()
    {
        var repeatAttack = Card(
            "persistent-cap", CardType.Attack, cost: 0,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 1),
            new CardEffect("retainSelf", EffectScope.Self, null, 0));
        var unit = CardUnit(
            "persistent-cap-unit", [new UnitDeckEntry("persistent-cap")],
            drawPerTurn: 1, handLimit: 1);
        var actor = OwnedSummon(
            "persistent-cap-instance", "persistent-cap-unit", 1);
        var state = BattleFixtures.MinimalState(
            allies: [BattleFixtures.Hero(), actor]);
        state = BattleParticipantAccess.InitializePrimary(state);
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [repeatAttack], units: [unit]);

        var first = BattleCpuPlanningCoordinator.PlanUntilReady(
            state, new MinimumRng(), catalog);
        var cpu = first.State.Participants.Single(
            p => p.ParticipantId == "cpu:persistent-cap-instance");
        var reopened = BattleParticipantAccess.Replace(
            first.State, cpu with { IsReady = false });
        var second = BattleCpuPlanningCoordinator.PlanUntilReady(
            reopened, new MinimumRng(), catalog);

        Assert.Empty(second.Actions);
        Assert.Contains(second.Stops, stop =>
            stop.ParticipantId == cpu.ParticipantId
            && stop.Reason == CpuPlanningStopReason.PerActorPlayCap
            && stop.PlaysCommitted == BattleCpuPlanningCoordinator.MaxPlaysPerActor);
        Assert.Equal(
            BattleCpuPlanningCoordinator.MaxPlaysPerActor,
            second.State.Participants.Single(
                p => p.ParticipantId == cpu.ParticipantId)
                .CpuCardPlaysThisTurn);
    }

    [Fact]
    public void PlanUntilReady_enforces_global_play_cap_and_readies_remaining_cpus()
    {
        var repeatAttack = Card(
            "global-repeat", CardType.Attack, cost: 0,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 1),
            new CardEffect("retainSelf", EffectScope.Self, null, 0));
        var units = Enumerable.Range(1, 4)
            .Select(index => CardUnit(
                $"global-unit-{index}",
                [new UnitDeckEntry("global-repeat")],
                drawPerTurn: 1,
                handLimit: 1))
            .ToArray();
        var allies = ImmutableArray.CreateBuilder<CombatActor>();
        allies.Add(BattleFixtures.Hero());
        for (int i = 1; i <= 4; i++)
            allies.Add(OwnedSummon(
                $"global-instance-{i}", $"global-unit-{i}", i));
        var state = BattleFixtures.MinimalState(allies: allies.ToImmutable());
        state = BattleParticipantAccess.InitializePrimary(state);
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [repeatAttack], units: units);

        var result = BattleCpuPlanningCoordinator.PlanUntilReady(
            state, new MinimumRng(), catalog);

        Assert.Equal(BattleCpuPlanningCoordinator.MaxTotalPlays, result.Actions.Length);
        Assert.Contains(result.Stops, stop =>
            stop.Reason == CpuPlanningStopReason.TotalPlayCap);
        Assert.All(
            result.State.Participants.Where(p => p.ControllerKind == BattleParticipantControllerKind.Cpu),
            participant => Assert.True(participant.IsReady));
    }

    [Fact]
    public void Battle_wide_play_cap_survives_dead_cpu_cleanup_and_later_planning_calls()
    {
        var fatal = Card(
            "fatal-cpu-action", CardType.Skill, cost: 0,
            new CardEffect("selfDamage", EffectScope.Self, null, 5));
        var repeat = Card(
            "survivor-repeat", CardType.Attack, cost: 0,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 1),
            new CardEffect("retainSelf", EffectScope.Self, null, 0));
        var fragileUnit = CardUnit(
            "fragile-cap-unit", [new UnitDeckEntry(fatal.Id)],
            drawPerTurn: 1, handLimit: 1);
        var survivorUnit = CardUnit(
            "survivor-cap-unit", [new UnitDeckEntry(repeat.Id)],
            drawPerTurn: 1, handLimit: 1);
        var fragile = OwnedSummon(
            "fragile-cap-instance", fragileUnit.Id, 1) with
        {
            CurrentHp = 1,
            MaxHp = 1,
        };
        var survivor = OwnedSummon(
            "survivor-cap-instance", survivorUnit.Id, 2);
        var state = BattleParticipantAccess.InitializePrimary(
            BattleFixtures.MinimalState(
                allies: [BattleFixtures.Hero(), fragile, survivor])) with
        {
            CpuTotalCardPlaysThisTurn =
                BattleCpuPlanningCoordinator.MaxTotalPlays - 1,
        };
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [fatal, repeat], units: [fragileUnit, survivorUnit]);

        var first = BattleCpuPlanningCoordinator.PlanUntilReady(
            state, new MinimumRng(), catalog);

        Assert.Single(first.Actions);
        Assert.Equal(
            BattleCpuPlanningCoordinator.MaxTotalPlays,
            first.State.CpuTotalCardPlaysThisTurn);
        Assert.DoesNotContain(first.State.Allies, actor =>
            actor.InstanceId == fragile.InstanceId);
        Assert.DoesNotContain(first.State.Participants, participant =>
            participant.ActorInstanceId == fragile.InstanceId);
        var readySurvivor = first.State.Participants.Single(participant =>
            participant.ActorInstanceId == survivor.InstanceId);
        Assert.True(readySurvivor.IsReady);

        var reopened = BattleParticipantAccess.Replace(
            first.State, readySurvivor with { IsReady = false });
        var second = BattleCpuPlanningCoordinator.PlanUntilReady(
            reopened, new MinimumRng(), catalog);

        Assert.Empty(second.Actions);
        Assert.Equal(
            BattleCpuPlanningCoordinator.MaxTotalPlays,
            second.State.CpuTotalCardPlaysThisTurn);
        Assert.True(second.State.Participants.Single(participant =>
            participant.ActorInstanceId == survivor.InstanceId).IsReady);
    }

    [Fact]
    public void Future_resource_change_prevents_false_repeated_state_stop()
    {
        var growthLoop = Card(
            "future-energy-loop", CardType.Skill, cost: 0,
            new CardEffect("gainMaxEnergy", EffectScope.Self, null, 1),
            new CardEffect("retainSelf", EffectScope.Self, null, 0));
        var unit = CardUnit(
            "future-energy-unit", [new UnitDeckEntry(growthLoop.Id)],
            drawPerTurn: 1, handLimit: 1);
        var actor = OwnedSummon("future-energy-instance", unit.Id, 1);
        var state = BattleParticipantAccess.InitializePrimary(
            BattleFixtures.MinimalState(
                allies: [BattleFixtures.Hero(), actor]));
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [growthLoop], units: [unit]);

        var result = BattleCpuPlanningCoordinator.PlanUntilReady(
            state, new MinimumRng(), catalog);

        Assert.Equal(
            BattleCpuPlanningCoordinator.MaxPlaysPerActor,
            result.Actions.Length);
        Assert.Contains(result.Stops, stop =>
            stop.ParticipantId == "cpu:future-energy-instance"
            && stop.Reason == CpuPlanningStopReason.PerActorPlayCap);
    }

    [Fact]
    public void Repeated_state_stop_is_not_reopened_by_unrelated_external_change()
    {
        var noOpLoop = Card(
            "stable-no-op-loop", CardType.Skill, cost: 0,
            new CardEffect("retainSelf", EffectScope.Self, null, 0));
        var unit = CardUnit(
            "stable-no-op-unit", [new UnitDeckEntry(noOpLoop.Id)],
            drawPerTurn: 1, handLimit: 1);
        var actor = OwnedSummon("stable-no-op-instance", unit.Id, 1);
        var state = BattleParticipantAccess.InitializePrimary(
            BattleFixtures.MinimalState(
                allies: [BattleFixtures.Hero(), actor]));
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [noOpLoop], units: [unit]);
        var first = BattleCpuPlanningCoordinator.PlanUntilReady(
            state, new MinimumRng(), catalog);
        Assert.Contains(first.Stops, stop =>
            stop.ParticipantId == "cpu:stable-no-op-instance"
            && stop.Reason == CpuPlanningStopReason.RepeatedState);
        var unrelated = first.State with
        {
            Enemies = first.State.Enemies.SetItem(
                0,
                first.State.Enemies[0] with
                {
                    Block = BlockPool.Empty.Add(9),
                }),
        };

        var second = BattleCpuPlanningCoordinator.PlanAfterExternalStateChange(
            unrelated, new MinimumRng(), catalog);

        Assert.Empty(second.Actions);
        Assert.Empty(second.ReopenedParticipantIds);
        Assert.True(second.State.Participants.Single(participant =>
            participant.ActorInstanceId == actor.InstanceId).IsReady);
    }

    [Fact]
    public void Terminal_state_does_not_initialize_cpu_or_consume_rng()
    {
        var card = Card("terminal-card", CardType.Skill, cost: 0);
        var unit = CardUnit(
            "terminal-unit", [new UnitDeckEntry(card.Id)],
            drawPerTurn: 1, handLimit: 1);
        var actor = OwnedSummon("terminal-instance", unit.Id, 1);
        var terminal = BattleParticipantAccess.InitializePrimary(
            BattleFixtures.MinimalState(
                allies: [BattleFixtures.Hero(), actor])) with
        {
            Phase = BattlePhase.Resolved,
            Outcome = BattleOutcome.Defeat,
        };
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [card], units: [unit]);

        var ensured = BattleCpuPlanningCoordinator.EnsureParticipants(
            terminal, new ThrowingRng(), catalog);
        var planned = BattleCpuPlanningCoordinator.PlanUntilReady(
            terminal, new ThrowingRng(), catalog);

        Assert.Same(terminal, ensured);
        Assert.Same(terminal, planned.State);
        Assert.Empty(planned.Actions);
        Assert.Empty(planned.Stops);
        Assert.Single(planned.State.Participants);
    }

    private static CardDefinition Card(
        string id,
        CardType type,
        int cost,
        params CardEffect[] effects) =>
        new(
            id, id, null, CardRarity.Token, type,
            cost, null,
            effects,
            UpgradedEffects: null,
            Keywords: null);

    private static UnitDefinition CardUnit(
        string id,
        IReadOnlyList<UnitDeckEntry> deck,
        int initialEnergy = 1,
        int drawPerTurn = 3,
        int handLimit = 5,
        int? lifetime = null,
        string initialMoveId = "",
        IReadOnlyList<CharacterSkillDefinition>? skills = null,
        IReadOnlyList<string>? ownedRelicIds = null,
        IReadOnlyList<string>? potions = null) =>
        new(
            id, id, id, 30,
            InitialMoveId: initialMoveId,
            Moves: [],
            LifetimeTurns: lifetime,
            Deck: deck,
            InitialEnergy: initialEnergy,
            EnergyPerTurn: 1,
            DrawPerTurn: drawPerTurn,
            HandLimit: handLimit,
            AiProfileId: UnitAiProfileIds.LowCostCombo,
            Skills: skills,
            OwnedRelicIds: ownedRelicIds,
            Potions: potions);

    private static CombatActor OwnedSummon(
        string instanceId,
        string definitionId,
        int slotIndex) =>
        BattleFixtures.SummonActor(instanceId, definitionId, slotIndex, hp: 30) with
        {
            OwnerPlayerSlot = 0,
            OwnerActorInstanceId = "hero_inst",
        };

    private sealed class MinimumRng : IRng
    {
        public int NextInt(int minInclusive, int maxExclusive) => minInclusive;
        public double NextDouble() => 0;
    }

    private sealed class ThrowingRng : IRng
    {
        public int NextInt(int minInclusive, int maxExclusive) =>
            throw new Xunit.Sdk.XunitException("terminal planning consumed RNG");

        public double NextDouble() =>
            throw new Xunit.Sdk.XunitException("terminal planning consumed RNG");
    }
}
