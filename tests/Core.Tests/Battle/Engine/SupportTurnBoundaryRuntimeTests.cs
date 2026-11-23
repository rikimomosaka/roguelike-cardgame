using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public sealed class SupportTurnBoundaryRuntimeTests
{
    [Fact]
    public void Combo_carryover_preserves_count_and_history_but_resets_cost_and_wild_state()
    {
        var snapshot = Snapshot("strike", 1);
        var hero = WithStatuses(
            BattleFixtures.Hero(),
            (BattleStatusIds.ComboCarryover, 1));
        var state = BattleFixtures.MinimalState(allies: [hero]) with
        {
            ComboCount = 3,
            MaxComboThisTurn = 7,
            ComboGauge = 5,
            LastPlayedOrigCost = 2,
            NextCardComboFreePass = true,
            WildUsedInCurrentCombo = true,
            CurrentComboCardHistory = [snapshot],
            PlayedCardHistoryThisTurn = [snapshot],
            LastFinisherRecord = [snapshot],
        };
        var catalog = ZeroDamageCatalog();

        var (after, events) = BattleEngine.EndTurn(
            state,
            new FakeRng([], []),
            catalog);

        Assert.Equal(3, after.ComboCount);
        Assert.Equal(3, after.MaxComboThisTurn);
        Assert.Equal(5, after.ComboGauge);
        Assert.Null(after.LastPlayedOrigCost);
        Assert.False(after.NextCardComboFreePass);
        Assert.False(after.WildUsedInCurrentCombo);
        var carriedSnapshot = Assert.Single(after.CurrentComboCardHistory);
        Assert.Equal(snapshot.CardInstanceId, carriedSnapshot.CardInstanceId);
        Assert.Equal(snapshot.PlaySequence, carriedSnapshot.PlaySequence);
        Assert.Empty(after.PlayedCardHistoryThisTurn);
        Assert.Empty(after.LastFinisherRecord);
        Assert.True(after.ComboCarryoverPending);
        Assert.Equal(0, after.Allies[0].GetStatus(BattleStatusIds.ComboCarryover));
        Assert.Contains(events, battleEvent =>
            battleEvent.Note == BattleStatusIds.ComboCarryover);
    }

    [Fact]
    public void Combo_carryover_spends_a_stack_and_arms_the_opening_even_when_combo_is_zero()
    {
        var hero = WithStatuses(
            BattleFixtures.Hero(),
            (BattleStatusIds.ComboCarryover, 2));
        var state = BattleFixtures.MinimalState(allies: [hero]) with
        {
            ComboCount = 0,
            MaxComboThisTurn = 5,
            LastPlayedOrigCost = 2,
            NextCardComboFreePass = true,
            WildUsedInCurrentCombo = true,
        };

        var (after, _) = BattleEngine.EndTurn(
            state,
            new FakeRng([], []),
            ZeroDamageCatalog());

        Assert.Equal(0, after.ComboCount);
        Assert.Equal(0, after.MaxComboThisTurn);
        Assert.Null(after.LastPlayedOrigCost);
        Assert.False(after.NextCardComboFreePass);
        Assert.False(after.WildUsedInCurrentCombo);
        Assert.True(after.ComboCarryoverPending);
        Assert.Equal(1, after.Allies[0].GetStatus(BattleStatusIds.ComboCarryover));
    }

    [Fact]
    public void Afterglow_preserves_all_turn_end_buffs_then_disappears()
    {
        var hero = WithStatuses(
            BattleFixtures.Hero() with { Block = new BlockPool(7, 0) },
            (BattleStatusIds.Afterglow, 1),
            (BattleStatusIds.Vigor, 1),
            (BattleStatusIds.Steadfast, 1));
        var state = BattleFixtures.MinimalState(allies: [hero]);

        var (after, events) = BattleEngine.EndTurn(
            state,
            new FakeRng([], []),
            ZeroDamageCatalog());

        Assert.Equal(1, after.Allies[0].GetStatus(BattleStatusIds.Vigor));
        Assert.Equal(1, after.Allies[0].GetStatus(BattleStatusIds.Steadfast));
        Assert.Equal(0, after.Allies[0].GetStatus(BattleStatusIds.Afterglow));
        Assert.Equal(3, after.Allies[0].Block.Display(0));
        Assert.Contains(events, battleEvent =>
            battleEvent.Note == BattleStatusIds.Afterglow);
    }

    [Fact]
    public void Afterglow_remains_when_there_is_no_eligible_positive_buff()
    {
        var hero = WithStatuses(
            BattleFixtures.Hero(),
            (BattleStatusIds.Afterglow, 1),
            (BattleStatusIds.Disruption, 1));
        var state = BattleFixtures.MinimalState(allies: [hero]);

        var (after, _) = BattleEngine.EndTurn(
            state,
            new FakeRng([], []),
            ZeroDamageCatalog());

        Assert.Equal(1, after.Allies[0].GetStatus(BattleStatusIds.Afterglow));
        Assert.Equal(0, after.Allies[0].GetStatus(BattleStatusIds.Disruption));
    }

    [Fact]
    public void Afterglow_preserves_combo_carryover_stack_while_the_combo_still_carries()
    {
        var hero = WithStatuses(
            BattleFixtures.Hero(),
            (BattleStatusIds.Afterglow, 1),
            (BattleStatusIds.ComboCarryover, 1));
        var state = BattleFixtures.MinimalState(allies: [hero]) with
        {
            ComboCount = 2,
            MaxComboThisTurn = 4,
            LastPlayedOrigCost = 1,
        };

        var (after, _) = BattleEngine.EndTurn(
            state,
            new FakeRng([], []),
            ZeroDamageCatalog());

        Assert.Equal(2, after.ComboCount);
        Assert.Equal(2, after.MaxComboThisTurn);
        Assert.Equal(1, after.Allies[0].GetStatus(BattleStatusIds.ComboCarryover));
        Assert.Equal(0, after.Allies[0].GetStatus(BattleStatusIds.Afterglow));
    }

    [Fact]
    public void Afterglow_does_not_disappear_for_standard_buff_added_after_side_countdown()
    {
        var hero = WithStatuses(
            BattleFixtures.Hero(),
            (BattleStatusIds.Afterglow, 1),
            (BattleStatusIds.Disruption, 1));
        var state = BattleFixtures.MinimalState(allies: [hero]);

        var (afterCountdown, _) = SideStatusCountdown.ApplyForSide(
            state,
            ActorSide.Ally,
            0);
        var heroWithLateVigor = WithStatuses(
            afterCountdown.Allies[0],
            (BattleStatusIds.Vigor, 1));
        var beforeFormalBoundary = afterCountdown with
        {
            Allies = afterCountdown.Allies.SetItem(0, heroWithLateVigor),
        };

        var (after, events) = FormalTurnEndStatusProcessor.Process(
            beforeFormalBoundary);

        Assert.Equal(1, after.Allies[0].GetStatus(BattleStatusIds.Vigor));
        Assert.Equal(1, after.Allies[0].GetStatus(BattleStatusIds.Afterglow));
        Assert.Empty(after.AfterglowProtectedActorInstanceIds);
        Assert.DoesNotContain(events, battleEvent =>
            battleEvent.Note == BattleStatusIds.Afterglow);
    }

    [Fact]
    public void Afterglow_disappears_when_protected_standard_buff_was_removed_later()
    {
        var hero = WithStatuses(
            BattleFixtures.Hero(),
            (BattleStatusIds.Afterglow, 1),
            (BattleStatusIds.Vigor, 1));
        var state = BattleFixtures.MinimalState(allies: [hero]);

        var (afterCountdown, _) = SideStatusCountdown.ApplyForSide(
            state,
            ActorSide.Ally,
            0);
        Assert.Contains(
            hero.InstanceId,
            afterCountdown.AfterglowProtectedActorInstanceIds);
        var heroWithoutVigor = afterCountdown.Allies[0] with
        {
            Statuses = afterCountdown.Allies[0].Statuses.Remove(
                BattleStatusIds.Vigor),
            StatusSources = afterCountdown.Allies[0].StatusSources.Remove(
                BattleStatusIds.Vigor),
        };
        var beforeFormalBoundary = afterCountdown with
        {
            Allies = afterCountdown.Allies.SetItem(0, heroWithoutVigor),
        };

        var (after, events) = FormalTurnEndStatusProcessor.Process(
            beforeFormalBoundary);

        Assert.Equal(0, after.Allies[0].GetStatus(BattleStatusIds.Afterglow));
        Assert.Empty(after.AfterglowProtectedActorInstanceIds);
        Assert.Contains(events, battleEvent =>
            battleEvent.Note == BattleStatusIds.Afterglow);
    }

    [Fact]
    public void Afterglow_disappears_when_it_protects_custom_countdown_only()
    {
        var hero = WithStatuses(
            BattleFixtures.Hero(),
            (BattleStatusIds.Afterglow, 1),
            (BattleStatusIds.Cover, 1));
        var state = BattleFixtures.MinimalState(allies: [hero]);

        var (after, events) = FormalTurnEndStatusProcessor.Process(state);

        Assert.Equal(1, after.Allies[0].GetStatus(BattleStatusIds.Cover));
        Assert.Equal(0, after.Allies[0].GetStatus(BattleStatusIds.Afterglow));
        Assert.Empty(after.AfterglowProtectedActorInstanceIds);
        Assert.Contains(events, battleEvent =>
            battleEvent.Note == BattleStatusIds.Afterglow);
    }

    [Fact]
    public void Secondary_participant_keeps_its_own_combo_after_final_stack_is_removed()
    {
        const string secondaryId = "cpu:wisp";
        var hero = BattleFixtures.Hero();
        var wisp = WithStatuses(
            BattleFixtures.SummonActor("wisp", "wisp", 1, hp: 30),
            (BattleStatusIds.ComboCarryover, 1));
        var state = BattleFixtures.MinimalState(allies: [hero, wisp]);
        state = BattleParticipantAccess.InitializePrimary(state);
        var secondary = state.Participants[0] with
        {
            ParticipantId = secondaryId,
            ActorInstanceId = wisp.InstanceId,
            ControllerKind = BattleParticipantControllerKind.Cpu,
            CharacterId = "wisp",
            Energy = 0,
            EnergyPerTurn = 1,
            DrawPerTurn = 0,
            ComboCount = 3,
            MaxComboThisTurn = 6,
            LastPlayedOrigCost = 2,
            WildUsedInCurrentCombo = true,
            CurrentComboCardHistory = [Snapshot("strike", 4)],
            Hand = [],
            DrawPile = [],
            DiscardPile = [],
            ExhaustPile = [],
        };
        state = BattleParticipantAccess.Add(state, secondary);
        var unit = new UnitDefinition(
            "wisp",
            "Wisp",
            "wisp",
            30,
            string.Empty,
            [],
            Deck: [new UnitDeckEntry("strike")],
            InitialEnergy: 1,
            EnergyPerTurn: 1,
            DrawPerTurn: 0,
            HandLimit: 3,
            AiProfileId: UnitAiProfileIds.LowCostCombo);
        var catalog = BattleFixtures.MinimalCatalog(units: [unit]);

        var afterEnd = BattleParticipantTurnLifecycle
            .ApplyEndToSecondaryParticipants(state, catalog);
        var (afterStatuses, _) = FormalTurnEndStatusProcessor.Process(afterEnd);
        var afterStart = BattleParticipantTurnLifecycle.ApplyStartToAllParticipants(
            afterStatuses,
            new FakeRng([], []),
            catalog);
        var carried = Assert.Single(afterStart.Participants, participant =>
            participant.ParticipantId == secondaryId);

        Assert.Equal(3, carried.ComboCount);
        Assert.Equal(3, carried.MaxComboThisTurn);
        Assert.Null(carried.LastPlayedOrigCost);
        Assert.False(carried.WildUsedInCurrentCombo);
        Assert.Single(carried.CurrentComboCardHistory);
        Assert.True(carried.ComboCarryoverPending);
        Assert.Equal(0, afterStart.Allies[1].GetStatus(
            BattleStatusIds.ComboCarryover));
    }

    private static PlayedCardEffectSnapshot Snapshot(string id, long sequence) =>
        new(
            id,
            $"{id}-{sequence}",
            "hero_inst",
            CardType.Attack,
            OriginalCost: 1,
            PaidCost: 1,
            IsUpgraded: false,
            Keywords: [],
            Effects: [new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 6)],
            PlaySequence: sequence);

    private static CombatActor WithStatuses(
        CombatActor actor,
        params (string Id, int Amount)[] statuses) => actor with
    {
        Statuses = statuses.Aggregate(
            actor.Statuses,
            (current, status) => current.SetItem(status.Id, status.Amount)),
    };

    private static RoguelikeCardGame.Core.Data.DataCatalog ZeroDamageCatalog()
    {
        var enemy = BattleFixtures.GoblinDef(attack: 0);
        return BattleFixtures.MinimalCatalog(enemies: [enemy]);
    }
}
