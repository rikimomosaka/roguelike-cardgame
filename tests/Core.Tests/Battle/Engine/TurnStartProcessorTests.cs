using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class TurnStartProcessorTests
{
    private static BattleState MakeState(
        ImmutableArray<BattleCardInstance> drawPile,
        ImmutableArray<BattleCardInstance>? hand = null,
        int turn = 1, int energy = 0, int energyMax = 3)
        => new(
            Turn: turn, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(BattleFixtures.Hero()),
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: energy, EnergyMax: energyMax,
            DrawPile: drawPile,
            Hand: hand ?? ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0,
            LastPlayedOrigCost: null,
            NextCardComboFreePass: false,
            OwnedRelicIds: ImmutableArray<string>.Empty,
            Potions: ImmutableArray<string>.Empty,
            EncounterId: "enc_test");

    private static ImmutableArray<BattleCardInstance> Deck(int n) =>
        Enumerable.Range(0, n)
            .Select(i => BattleFixtures.MakeBattleCard("strike", $"c{i}"))
            .ToImmutableArray();

    [Fact] public void Increments_turn()
    {
        var s = MakeState(Deck(10), turn: 1);
        var rng = new FakeRng(new int[100], new double[0]); // shuffle 不要、すでに 10 枚
        var (next, _) = TurnStartProcessor.Process(s, rng, BattleFixtures.MinimalCatalog());
        Assert.Equal(2, next.Turn);
    }

    [Theory]
    [InlineData(BattleOutcome.Victory, BattlePhase.PlayerInput)]
    [InlineData(BattleOutcome.Pending, BattlePhase.Resolved)]
    public void Terminal_state_is_a_no_op(
        BattleOutcome outcome, BattlePhase phase)
    {
        var state = MakeState(
            Deck(10),
            hand: ImmutableArray.Create(
                BattleFixtures.MakeBattleCard("strike", "held")),
            turn: 4,
            energy: 2,
            energyMax: 3) with
        {
            Outcome = outcome,
            Phase = phase,
        };
        var rng = new FakeRng(new int[100], new double[0]);

        var (next, events) = TurnStartProcessor.Process(
            state, rng, BattleFixtures.MinimalCatalog());

        Assert.Same(state, next);
        Assert.Empty(events);
        Assert.Equal(4, next.Turn);
        Assert.Equal(2, next.Energy);
        Assert.Single(next.Hand);
        Assert.Equal(10, next.DrawPile.Length);
    }

    [Fact]
    public void Resets_turn_max_combo_even_when_called_directly()
    {
        var s = MakeState(Deck(10)) with
        {
            ComboCount = 3,
            MaxComboThisTurn = 6,
            LastPlayedOrigCost = 2,
            NextCardComboFreePass = true,
            WildUsedInCurrentCombo = true,
        };
        var rng = new FakeRng(new int[100], new double[0]);

        var (next, _) = TurnStartProcessor.Process(s, rng, BattleFixtures.MinimalCatalog());

        Assert.Equal(0, next.ComboCount);
        Assert.Equal(0, next.MaxComboThisTurn);
        Assert.Null(next.LastPlayedOrigCost);
        Assert.False(next.NextCardComboFreePass);
        Assert.False(next.WildUsedInCurrentCombo);
    }

    [Theory]
    [InlineData(0, 3, 3)]
    [InlineData(2, 3, 5)]
    [InlineData(5, 4, 9)]
    [InlineData(int.MaxValue - 1, 3, int.MaxValue)]
    [InlineData(int.MaxValue, 3, int.MaxValue)]
    public void Adds_energy_per_turn_to_carried_energy_without_overflow(
        int currentEnergy, int energyPerTurn, int expected)
    {
        var s = MakeState(Deck(10), energy: currentEnergy, energyMax: energyPerTurn);
        var rng = new FakeRng(new int[100], new double[0]);
        var (next, _) = TurnStartProcessor.Process(s, rng, BattleFixtures.MinimalCatalog());
        Assert.Equal(expected, next.Energy);
    }

    [Fact]
    public void Zero_energy_per_turn_preserves_carried_energy()
    {
        var s = MakeState(Deck(10), energy: 7, energyMax: 0);
        var rng = new FakeRng(new int[100], new double[0]);

        var (next, _) = TurnStartProcessor.Process(s, rng, BattleFixtures.MinimalCatalog());

        Assert.Equal(7, next.Energy);
    }

    [Fact] public void Draws_five_cards_when_draw_pile_sufficient()
    {
        var s = MakeState(Deck(10));
        var rng = new FakeRng(new int[100], new double[0]);
        var (next, _) = TurnStartProcessor.Process(s, rng, BattleFixtures.MinimalCatalog());
        Assert.Equal(5, next.Hand.Length);
        Assert.Equal(5, next.DrawPile.Length);
    }

    [Fact] public void Reshuffles_discard_into_draw_when_empty()
    {
        var hand = ImmutableArray<BattleCardInstance>.Empty;
        var s = MakeState(Deck(2), hand) with { DiscardPile = Deck(5) };
        // ハンドに既に 0 枚、山札 2 枚、捨札 5 枚 → 5 枚ドロー要求
        // 山札 2 枚 ドロー → 山札 0 枚 → 捨札 5 枚をシャッフルして山札へ → 残り 3 枚ドロー
        var rng = new FakeRng(new int[] { 0, 0, 0, 0, 0 }, new double[0]); // Fisher-Yates 用
        var (next, _) = TurnStartProcessor.Process(s, rng, BattleFixtures.MinimalCatalog());
        Assert.Equal(5, next.Hand.Length);
        Assert.Empty(next.DiscardPile);
    }

    [Fact] public void Stops_when_both_piles_empty()
    {
        var s = MakeState(Deck(2));
        var rng = new FakeRng(new int[100], new double[0]);
        var (next, _) = TurnStartProcessor.Process(s, rng, BattleFixtures.MinimalCatalog());
        Assert.Equal(2, next.Hand.Length);
        Assert.Empty(next.DrawPile);
    }

    [Fact] public void Stops_at_hand_cap_of_ten()
    {
        var hand = Enumerable.Range(0, 8)
            .Select(i => BattleFixtures.MakeBattleCard("strike", $"h{i}"))
            .ToImmutableArray();
        var s = MakeState(Deck(10), hand);
        var rng = new FakeRng(new int[100], new double[0]);
        var (next, _) = TurnStartProcessor.Process(s, rng, BattleFixtures.MinimalCatalog());
        Assert.Equal(10, next.Hand.Length); // 8 既存 + 2 ドロー (5 ではなく 10 でストップ)
        Assert.Equal(8, next.DrawPile.Length); // 10 - 2 = 8 残り
    }

    [Fact] public void Emits_TurnStart_event()
    {
        var s = MakeState(Deck(10));
        var rng = new FakeRng(new int[100], new double[0]);
        var (_, events) = TurnStartProcessor.Process(s, rng, BattleFixtures.MinimalCatalog());
        Assert.Contains(events, e => e.Kind == BattleEventKind.TurnStart);
    }
}
