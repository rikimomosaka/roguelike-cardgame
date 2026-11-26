using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class BattleParticipantTurnLifecycleTests
{
    [Fact]
    public void Secondary_end_discards_non_retained_cards_and_resets_turn_state()
    {
        var retain = new CardDefinition(
            "retain", "Retain", null, CardRarity.Token, CardType.Skill,
            0, null,
            [new("retainSelf", EffectScope.Self, null, 0)],
            null, null);
        var plain = BattleFixtures.Strike();
        var (state, participantId, unit) = MakeState(
            hand:
            [
                new("keep", "retain", false, null),
                new("drop", "strike", false, null),
            ],
            draw: [],
            energy: 2,
            isReady: true);
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [retain, plain], units: [unit]);

        var result = BattleParticipantTurnLifecycle
            .ApplyEndToSecondaryParticipants(state, catalog);
        var participant = Assert.Single(
            result.Participants, value => value.ParticipantId == participantId);

        Assert.Equal(["keep"], participant.Hand.Select(card => card.InstanceId));
        Assert.Equal(["drop"], participant.DiscardPile.Select(card => card.InstanceId));
        Assert.Equal(0, participant.ComboCount);
        Assert.Empty(participant.CardUsesThisTurn);
        Assert.True(participant.IsReady);
        Assert.Equal(state.Participants[0].Hand, result.Hand);
    }

    [Fact]
    public void Participant_start_carries_energy_draws_to_its_hand_limit_and_unreadies_everyone()
    {
        var draw = ImmutableArray.Create(
            new BattleCardInstance("d1", "strike", false, null),
            new BattleCardInstance("d2", "strike", false, null),
            new BattleCardInstance("d3", "strike", false, null),
            new BattleCardInstance("d4", "strike", false, null));
        var (state, participantId, unit) = MakeState(
            hand: [new("held", "strike", false, null)],
            draw: draw,
            energy: 2,
            isReady: true);
        state = state with
        {
            Participants = state.Participants
                .Select(participant => participant with { IsReady = true })
                .ToImmutableArray(),
        };
        var catalog = BattleFixtures.MinimalCatalog(units: [unit]);

        var result = BattleParticipantTurnLifecycle
            .ApplyStartToAllParticipants(
                state, new FakeRng([], []), catalog);
        var participant = Assert.Single(
            result.Participants, value => value.ParticipantId == participantId);

        // 残り 2 が規定値以上なのでそのまま（足し算はしない）。
        Assert.Equal(Math.Max(2, participant.EnergyPerTurn), participant.Energy);
        Assert.Equal(3, participant.Hand.Length);
        Assert.Equal(["held", "d1", "d2"],
            participant.Hand.Select(card => card.InstanceId));
        Assert.Equal(2, participant.DrawPile.Length);
        Assert.All(result.Participants, value => Assert.False(value.IsReady));
        Assert.Equal(
            Math.Max(state.Participants[0].Energy, state.Participants[0].EnergyPerTurn),
            result.Energy);
    }

    [Fact]
    public void Secondary_turn_start_statuses_change_only_its_owned_resources()
    {
        var draw = ImmutableArray.Create(
            new BattleCardInstance("d1", "strike", false, null),
            new BattleCardInstance("d2", "strike", false, null),
            new BattleCardInstance("d3", "strike", false, null));
        var (state, participantId, unit) = MakeState(
            hand: [], draw: draw, energy: 2, isReady: true);
        int actorIndex = Enumerable.Range(0, state.Allies.Length)
            .Single(index => state.Allies[index].InstanceId == "wisp");
        state = state with
        {
            Allies = state.Allies.SetItem(
                actorIndex,
                state.Allies[actorIndex] with
                {
                    Statuses = ImmutableDictionary<string, int>.Empty
                        .Add(BattleStatusIds.Insight, 1)
                        .Add(BattleStatusIds.Haste, 2)
                        .Add(BattleStatusIds.Mastery, 3)
                        .Add(BattleStatusIds.Exhaustion, 1),
                }),
        };
        int primaryEnergy = state.Energy;
        var catalog = BattleFixtures.MinimalCatalog(units: [unit]);

        var (afterStart, _) = TurnStartProcessor.Process(
            state, new FakeRng([], []), catalog);
        var statusParticipant = Assert.Single(
            afterStart.Participants,
            participant => participant.ParticipantId == participantId);
        var updatedActor = afterStart.Allies[actorIndex];

        Assert.Equal(3, statusParticipant.Energy); // 残り 2（規定値 1 以上なのでそのまま）+2 Haste -1 Exhaustion
        Assert.Equal(3, statusParticipant.ComboGauge);
        Assert.Equal(3, statusParticipant.Hand.Length);
        Assert.Equal(["d1", "d2", "d3"],
            statusParticipant.Hand.Select(card => card.InstanceId));
        Assert.Equal(0, updatedActor.GetStatus(BattleStatusIds.Insight));
        Assert.Equal(0, updatedActor.GetStatus(BattleStatusIds.Haste));
        Assert.Equal(2, updatedActor.GetStatus(BattleStatusIds.Mastery));
        Assert.Equal(0, updatedActor.GetStatus(BattleStatusIds.Exhaustion));
        Assert.Equal(Math.Max(primaryEnergy, state.EnergyMax), afterStart.Energy);
    }

    private static (BattleState State, string ParticipantId, UnitDefinition Unit)
        MakeState(
            ImmutableArray<BattleCardInstance> hand,
            ImmutableArray<BattleCardInstance> draw,
            int energy,
            bool isReady)
    {
        const string participantId = "cpu:wisp";
        var hero = BattleFixtures.Hero();
        var wisp = BattleFixtures.SummonActor("wisp", "wisp", 1, hp: 30);
        var state = BattleFixtures.MinimalState(allies: [hero, wisp]);
        state = BattleParticipantAccess.InitializePrimary(state);
        var secondary = state.Participants[0] with
        {
            ParticipantId = participantId,
            ActorInstanceId = wisp.InstanceId,
            ControllerKind = BattleParticipantControllerKind.Cpu,
            OwnerPlayerSlot = 0,
            CharacterId = "wisp",
            TargetAllyIndex = 1,
            Energy = energy,
            EnergyPerTurn = 1,
            DrawPerTurn = 3,
            DrawPile = draw,
            Hand = hand,
            DiscardPile = [],
            ExhaustPile = [],
            SummonHeld = [],
            PowerCards = [],
            ComboCount = 3,
            MaxComboThisTurn = 4,
            LastPlayedOrigCost = 2,
            CardUsesThisTurn = ImmutableDictionary<string, int>.Empty.Add("strike", 1),
            OwnedRelicIds = [],
            Potions = [],
            IsReady = isReady,
        };
        state = BattleParticipantAccess.Add(state, secondary);
        var unit = new UnitDefinition(
            "wisp", "Wisp", "wisp", 30, string.Empty, [],
            Deck: [new("strike", Count: 2)],
            InitialEnergy: 1,
            EnergyPerTurn: 1,
            DrawPerTurn: 3,
            HandLimit: 3,
            AiProfileId: UnitAiProfileIds.LowCostCombo);
        return (state, participantId, unit);
    }
}
