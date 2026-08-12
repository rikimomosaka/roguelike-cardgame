using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
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
            OwnedRelicIds: ImmutableArray<string>.Empty,
            Potions: ImmutableArray<string>.Empty,
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

    [Fact]
    public void PlayCard_uses_the_same_lowest_slot_living_hero_as_shared_hand_owner()
    {
        var highSlot = BattleFixtures.Hero() with
        {
            InstanceId = "hero-high",
            SlotIndex = 5,
            Statuses = BattleFixtures.Hero().Statuses
                .SetItem(BattleCardCostCalculator.HandCostIncreaseStatusId, 2),
        };
        var lowSlot = BattleFixtures.Hero() with
        {
            InstanceId = "hero-low",
            SlotIndex = 1,
        };
        var state = MakeState(
            ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "c1")),
            energy: 1) with
        {
            Allies = ImmutableArray.Create(highSlot, lowSlot),
        };

        var (next, events) = BattleEngine.PlayCard(
            state, 0, 0, 0, Rng(), BattleFixtures.MinimalCatalog());

        Assert.Equal(0, next.Energy);
        Assert.Equal("hero-low", events[0].CasterInstanceId);
        Assert.Equal(0, next.Allies[0].AttackSingle.Sum);
        Assert.Equal(6, next.Allies[1].AttackSingle.Sum);
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

    [Fact] public void Combo_protection_remains_false_for_card_without_wild_keyword()
    {
        var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "c1"));
        var s = MakeState(hand);
        var cat = BattleFixtures.MinimalCatalog();
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.False(next.NextCardComboFreePass);
        Assert.False(next.WildUsedInCurrentCombo);
    }

    [Fact]
    public void Random_discard_effect_never_discards_the_card_being_played()
    {
        var defensiveStrike = new CardDefinition(
            "defensive_strike", "Defensive Strike", null,
            CardRarity.Common, CardType.Attack,
            Cost: 1, UpgradedCost: null,
            Effects: new[]
            {
                new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5),
                new CardEffect("block", EffectScope.Self, EffectSide.Enemy, 4),
                // Current content uses Scope=Single with explicit Select=random.
                new CardEffect("discard", EffectScope.Single, EffectSide.Enemy, 1,
                    Select: "random"),
            },
            UpgradedEffects: null, Keywords: null);
        var hand = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard(defensiveStrike.Id, "played-card"),
            BattleFixtures.MakeBattleCard("defend", "discard-target"));
        var state = MakeState(hand);
        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { defensiveStrike, BattleFixtures.Defend() });

        // Index 0 used to select defensive strike itself and make finalization fail.
        var rng = new FakeRng(new[] { 0 }, new double[0]);
        var (next, events) = BattleEngine.PlayCard(state, 0, 0, 0, rng, catalog);

        Assert.Empty(next.Hand);
        Assert.Contains(next.DiscardPile, c => c.InstanceId == "discard-target");
        Assert.Contains(next.DiscardPile, c => c.InstanceId == "played-card");
        Assert.Equal(5, next.Allies[0].AttackSingle.Sum);
        Assert.Equal(4, next.Allies[0].Block.Sum);
        Assert.Contains(events, e => e.Kind == BattleEventKind.Discard && e.Amount == 1);
    }

    [Fact]
    public void Random_discard_with_no_other_hand_card_is_a_no_op_then_played_card_finalizes()
    {
        var discardCard = new CardDefinition(
            "discard_only", "Discard Only", null,
            CardRarity.Common, CardType.Skill,
            Cost: 0, UpgradedCost: null,
            Effects: new[]
            {
                new CardEffect("discard", EffectScope.Self, null, 1,
                    Select: "random"),
            },
            UpgradedEffects: null, Keywords: null);
        var state = MakeState(ImmutableArray.Create(
            BattleFixtures.MakeBattleCard(discardCard.Id, "played-card")));
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { discardCard });

        var (next, events) = BattleEngine.PlayCard(state, 0, null, null, Rng(), catalog);

        Assert.Empty(next.Hand);
        Assert.Collection(next.DiscardPile,
            card => Assert.Equal("played-card", card.InstanceId));
        Assert.DoesNotContain(events, e => e.Kind == BattleEventKind.Discard);
    }

    [Fact]
    public void Choose_discard_auto_resolution_excludes_the_card_being_played()
    {
        var discardCard = new CardDefinition(
            "discard_choose", "Discard Choose", null,
            CardRarity.Common, CardType.Skill,
            Cost: 0, UpgradedCost: null,
            Effects: new[]
            {
                new CardEffect("discard", EffectScope.Self, null, 1,
                    Pile: "hand", Select: "choose"),
            },
            UpgradedEffects: null, Keywords: null);
        var state = MakeState(ImmutableArray.Create(
            BattleFixtures.MakeBattleCard(discardCard.Id, "played-card"),
            BattleFixtures.MakeBattleCard("defend", "discard-target")));
        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { discardCard, BattleFixtures.Defend() });

        // One eligible candidate means choose auto-resolves without opening a modal.
        var (next, events) = BattleEngine.PlayCard(
            state, 0, null, null,
            new FakeRng(new[] { 0 }, new double[0]), catalog);

        Assert.Null(next.PendingCardPlay);
        Assert.Empty(next.Hand);
        Assert.Contains(next.DiscardPile, c => c.InstanceId == "discard-target");
        Assert.Contains(next.DiscardPile, c => c.InstanceId == "played-card");
        Assert.Contains(events, e => e.Kind == BattleEventKind.Discard && e.Amount == 1);
    }

    [Fact]
    public void Random_exhaust_from_hand_excludes_the_card_being_played()
    {
        var exhaustCard = new CardDefinition(
            "exhaust_other", "Exhaust Other", null,
            CardRarity.Common, CardType.Skill,
            Cost: 0, UpgradedCost: null,
            Effects: new[]
            {
                new CardEffect("exhaustCard", EffectScope.Self, null, 1,
                    Pile: "hand", Select: "random"),
            },
            UpgradedEffects: null, Keywords: null);
        var state = MakeState(ImmutableArray.Create(
            BattleFixtures.MakeBattleCard(exhaustCard.Id, "played-card"),
            BattleFixtures.MakeBattleCard("defend", "exhaust-target")));
        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { exhaustCard, BattleFixtures.Defend() });

        var (next, events) = BattleEngine.PlayCard(
            state, 0, null, null,
            new FakeRng(new[] { 0 }, new double[0]), catalog);

        Assert.Empty(next.Hand);
        Assert.Contains(next.ExhaustPile, c => c.InstanceId == "exhaust-target");
        Assert.DoesNotContain(next.ExhaustPile, c => c.InstanceId == "played-card");
        Assert.Contains(next.DiscardPile, c => c.InstanceId == "played-card");
        Assert.Contains(events, e => e.Kind == BattleEventKind.Exhaust && e.Amount == 1);
    }

    [Fact]
    public void Upgrade_all_in_hand_excludes_the_card_being_played()
    {
        var upgradeCard = new CardDefinition(
            "upgrade_other", "Upgrade Other", null,
            CardRarity.Common, CardType.Skill,
            Cost: 0, UpgradedCost: 0,
            Effects: new[]
            {
                new CardEffect("upgrade", EffectScope.Self, null, 1,
                    Pile: "hand", Select: "all"),
            },
            UpgradedEffects: null, Keywords: null);
        var upgradeTarget = new CardDefinition(
            "upgrade_target", "Upgrade Target", null,
            CardRarity.Common, CardType.Skill,
            Cost: 1, UpgradedCost: 0,
            Effects: Array.Empty<CardEffect>(),
            UpgradedEffects: null, Keywords: null);
        var state = MakeState(ImmutableArray.Create(
            BattleFixtures.MakeBattleCard(upgradeCard.Id, "played-card"),
            BattleFixtures.MakeBattleCard(upgradeTarget.Id, "upgrade-target")));
        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { upgradeCard, upgradeTarget });

        var (next, events) = BattleEngine.PlayCard(state, 0, null, null, Rng(), catalog);

        Assert.True(Assert.Single(next.Hand).IsUpgraded);
        Assert.False(Assert.Single(next.DiscardPile).IsUpgraded);
        Assert.Contains(events, e => e.Kind == BattleEventKind.Upgrade && e.Amount == 1);
    }
}
