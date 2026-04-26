using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class BattleEnginePlayCardTests
{
    private static BattleState MakeState(
        ImmutableArray<BattleCardInstance> hand,
        int energy = 3)
        => new(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(BattleFixtures.Hero()),
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: energy, EnergyMax: 3,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: hand,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0,
            LastPlayedOrigCost: null,
            NextCardComboFreePass: false,
            EncounterId: "enc_test");

    private static IRng Rng() => new FakeRng(new int[10], new double[0]);

    [Fact] public void Pays_energy_cost()
    {
        var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "c1"));
        var s = MakeState(hand, energy: 3);
        var cat = BattleFixtures.MinimalCatalog();
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(2, next.Energy);
    }

    [Fact] public void Strike_adds_to_AttackSingle()
    {
        var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "c1"));
        var s = MakeState(hand);
        var cat = BattleFixtures.MinimalCatalog();
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(6, next.Allies[0].AttackSingle.Sum);
    }

    [Fact] public void Defend_adds_to_BlockPool()
    {
        var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard("defend", "c1"));
        var s = MakeState(hand);
        var cat = BattleFixtures.MinimalCatalog();
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(5, next.Allies[0].Block.Sum);
    }

    [Fact] public void Played_card_moves_to_discard()
    {
        var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "c1"));
        var s = MakeState(hand);
        var cat = BattleFixtures.MinimalCatalog();
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Empty(next.Hand);
        Assert.Single(next.DiscardPile);
        Assert.Equal("c1", next.DiscardPile[0].InstanceId);
    }

    [Fact] public void Throws_when_energy_insufficient()
    {
        var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "c1"));
        var s = MakeState(hand, energy: 0);
        var cat = BattleFixtures.MinimalCatalog();
        Assert.Throws<System.InvalidOperationException>(() =>
            BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat));
    }

    [Fact] public void Throws_when_not_in_PlayerInput_phase()
    {
        var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "c1"));
        var s = MakeState(hand) with { Phase = BattlePhase.PlayerAttacking };
        var cat = BattleFixtures.MinimalCatalog();
        Assert.Throws<System.InvalidOperationException>(() =>
            BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat));
    }

    [Fact] public void Emits_PlayCard_event_first()
    {
        var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "c1"));
        var s = MakeState(hand);
        var cat = BattleFixtures.MinimalCatalog();
        var (_, events) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(BattleEventKind.PlayCard, events[0].Kind);
        Assert.Equal("strike", events[0].CardId);
    }

    [Fact] public void Updates_LastPlayedOrigCost_to_card_cost()
    {
        var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "c1"));
        var s = MakeState(hand);
        var cat = BattleFixtures.MinimalCatalog();
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(1, next.LastPlayedOrigCost);
    }

    [Fact] public void Updates_ComboCount_to_one_on_first_play()
    {
        var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "c1"));
        var s = MakeState(hand);
        var cat = BattleFixtures.MinimalCatalog();
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(1, next.ComboCount);
    }

    [Fact] public void NextCardComboFreePass_remains_false_for_non_superwild()
    {
        var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "c1"));
        var s = MakeState(hand);
        var cat = BattleFixtures.MinimalCatalog();
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.False(next.NextCardComboFreePass);
    }
}
