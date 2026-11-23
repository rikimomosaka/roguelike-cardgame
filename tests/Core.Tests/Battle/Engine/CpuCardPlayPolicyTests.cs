using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class CpuCardPlayPolicyTests
{
    [Fact]
    public void Combo_discounted_continuation_beats_a_cheaper_breaker()
    {
        var continuation = Card("two", cost: 2);
        var breaker = Card("zero", cost: 0);
        var (state, catalog, participantId) = Setup(
            energy: 2,
            hand: [CardInstance("zero", "zero-1"), CardInstance("two", "two-1")],
            cards: [breaker, continuation],
            deck: [new("zero"), new("two")],
            lastPlayedOriginalCost: 1);

        var decision = CpuCardPlayPolicy.ChooseNext(state, participantId, catalog);

        Assert.NotNull(decision);
        Assert.Equal("two-1", decision.CardInstanceId);
        Assert.Equal(1, decision.PayableCost);
        Assert.True(decision.ComboContinues);
        Assert.True(decision.ComboReductionEligible);
        Assert.Equal(0, decision.ContinuationTier);
    }

    [Fact]
    public void Same_category_uses_payable_cost_then_priority_then_leftmost()
    {
        var cheap = Card("cheap", cost: 1);
        var priorityLow = Card("priority-low", cost: 2);
        var priorityHigh = Card("priority-high", cost: 2);
        var (state, catalog, participantId) = Setup(
            energy: 5,
            hand:
            [
                CardInstance("priority-low", "low-1"),
                CardInstance("priority-high", "high-1"),
                CardInstance("cheap", "cheap-left"),
                CardInstance("cheap", "cheap-right"),
            ],
            cards: [cheap, priorityLow, priorityHigh],
            deck:
            [
                new("cheap", Priority: 0),
                new("priority-low", Priority: 1),
                new("priority-high", Priority: 9),
            ],
            lastPlayedOriginalCost: null);

        var decision = CpuCardPlayPolicy.ChooseNext(state, participantId, catalog);

        Assert.NotNull(decision);
        Assert.Equal("cheap-left", decision.CardInstanceId);
        Assert.Equal(2, decision.HandIndex);
    }

    [Fact]
    public void Unaffordable_restricted_and_usage_exhausted_cards_are_not_candidates()
    {
        var expensive = Card("expensive", cost: 3);
        var limited = Card("limited", cost: 0, usesPerTurn: 1);
        var (state, catalog, participantId) = Setup(
            energy: 1,
            hand:
            [
                CardInstance("expensive", "expensive-1"),
                CardInstance("limited", "limited-1"),
            ],
            cards: [expensive, limited],
            deck: [new("expensive"), new("limited")],
            lastPlayedOriginalCost: null);
        var participant = BattleParticipantAccess.EffectiveParticipants(state)
            .Single(p => p.ParticipantId == participantId) with
        {
            CardUsesThisTurn = ImmutableDictionary<string, int>.Empty.Add("limited", 1),
        };
        state = BattleParticipantAccess.Replace(state, participant);

        Assert.Null(CpuCardPlayPolicy.ChooseNext(state, participantId, catalog));
    }

    [Fact]
    public void Single_attack_targets_a_killable_enemy_then_lowest_hp_then_slot()
    {
        var attack = new CardDefinition(
            "finisher", "finisher", null, CardRarity.Token, CardType.Attack,
            0, null,
            [new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 3)],
            null, null);
        var (state, catalog, participantId) = Setup(
            energy: 1,
            hand: [CardInstance("finisher", "finisher-1")],
            cards: [attack],
            deck: [new("finisher")],
            lastPlayedOriginalCost: null);
        state = state with
        {
            Enemies =
            [
                BattleFixtures.Goblin(slotIndex: 0, hp: 8),
                BattleFixtures.Goblin(slotIndex: 2, hp: 2),
                BattleFixtures.Goblin(slotIndex: 1, hp: 3),
            ],
        };

        var decision = CpuCardPlayPolicy.ChooseNext(state, participantId, catalog);

        Assert.NotNull(decision);
        Assert.Equal(1, decision.TargetEnemyIndex);
    }

    private static (BattleState State, RoguelikeCardGame.Core.Data.DataCatalog Catalog, string ParticipantId)
        Setup(
            int energy,
            ImmutableArray<BattleCardInstance> hand,
            CardDefinition[] cards,
            UnitDeckEntry[] deck,
            int? lastPlayedOriginalCost)
    {
        const string participantId = "cpu:wisp-1";
        var hero = BattleFixtures.Hero();
        var wisp = BattleFixtures.SummonActor("wisp-1", "wisp", slotIndex: 1, hp: 30);
        var state = BattleFixtures.MinimalState(
            allies: [hero, wisp],
            energy: 3);
        state = BattleParticipantAccess.InitializePrimary(state);
        var secondary = state.Participants[0] with
        {
            ParticipantId = participantId,
            ActorInstanceId = wisp.InstanceId,
            ControllerKind = BattleParticipantControllerKind.Cpu,
            OwnerPlayerSlot = 0,
            CharacterId = "wisp",
            Energy = energy,
            EnergyPerTurn = 1,
            DrawPerTurn = 3,
            DrawPile = [],
            Hand = hand,
            DiscardPile = [],
            ExhaustPile = [],
            SummonHeld = [],
            PowerCards = [],
            ComboCount = lastPlayedOriginalCost is null ? 0 : 1,
            MaxComboThisTurn = lastPlayedOriginalCost is null ? 0 : 1,
            LastPlayedOrigCost = lastPlayedOriginalCost,
            OwnedRelicIds = [],
            Potions = [],
            IsReady = false,
        };
        state = BattleParticipantAccess.Add(state, secondary);
        var unit = new UnitDefinition(
            "wisp", "Wisp", "wisp", 30,
            InitialMoveId: string.Empty,
            Moves: [],
            Deck: deck,
            InitialEnergy: 1,
            EnergyPerTurn: 1,
            DrawPerTurn: 3,
            HandLimit: 5,
            AiProfileId: UnitAiProfileIds.LowCostCombo);
        var catalog = BattleFixtures.MinimalCatalog(cards: cards, units: [unit]);
        return (state, catalog, participantId);
    }

    private static CardDefinition Card(string id, int cost, int usesPerTurn = 0) =>
        new(
            id, id, null, CardRarity.Token, CardType.Attack,
            cost, null,
            [new("attack", EffectScope.Single, EffectSide.Enemy, 1)],
            null, null,
            UsesPerTurn: usesPerTurn);

    private static BattleCardInstance CardInstance(string definitionId, string instanceId) =>
        new(instanceId, definitionId, false, null);
}
