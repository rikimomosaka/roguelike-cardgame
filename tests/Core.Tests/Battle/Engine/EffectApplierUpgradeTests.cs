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

public class EffectApplierUpgradeTests
{
    private static IRng Rng() => new FakeRng(new int[10], new double[0]);

    // upgrade-able カード (UpgradedCost あり)
    private static CardDefinition UpgradableStrike() =>
        new("strike", "Strike", null, CardRarity.Common, CardType.Attack,
            Cost: 1, UpgradedCost: 0,
            Effects: new[] { new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 6) },
            UpgradedEffects: null, Keywords: null);

    // upgrade 不可 (UpgradedCost / UpgradedEffects 両方 null)
    private static CardDefinition UnUpgradableCard() =>
        new("plain", "Plain", null, CardRarity.Common, CardType.Attack,
            Cost: 1, UpgradedCost: null,
            Effects: new[] { new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 1) },
            UpgradedEffects: null, Keywords: null);

    private static BattleState MakeState(
        ImmutableArray<BattleCardInstance> hand = default) =>
        new(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(BattleFixtures.Hero()),
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: 3, EnergyMax: 3,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: hand.IsDefault ? ImmutableArray<BattleCardInstance>.Empty : hand,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0, LastPlayedOrigCost: null, NextCardComboFreePass: false,
            EncounterId: "enc_test");

    [Fact] public void Upgrade_random_card_in_hand()
    {
        var hand = ImmutableArray.Create(
            new BattleCardInstance("c1", "strike", false, null));
        var s = MakeState(hand);
        var hero = s.Allies[0];
        var eff = new CardEffect("upgrade", EffectScope.Self, null, 1, Pile: "hand");
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { UpgradableStrike() });
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), cat);
        Assert.True(next.Hand[0].IsUpgraded);
        Assert.Equal(BattleEventKind.Upgrade, evs[0].Kind);
        Assert.Equal(1, evs[0].Amount);
        Assert.Equal("hand", evs[0].Note);
    }

    [Fact] public void Upgrade_skips_already_upgraded()
    {
        var hand = ImmutableArray.Create(
            new BattleCardInstance("c1", "strike", true, null),  // already upgraded
            new BattleCardInstance("c2", "strike", false, null)); // upgrade target
        var s = MakeState(hand);
        var hero = s.Allies[0];
        var eff = new CardEffect("upgrade", EffectScope.Self, null, 1, Pile: "hand");
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { UpgradableStrike() });
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), cat);
        Assert.True(next.Hand[0].IsUpgraded);  // unchanged
        Assert.True(next.Hand[1].IsUpgraded);  // newly upgraded
        Assert.Equal(1, evs[0].Amount);
    }

    [Fact] public void Upgrade_skips_unupgradable_definitions()
    {
        var hand = ImmutableArray.Create(
            new BattleCardInstance("c1", "plain", false, null));
        var s = MakeState(hand);
        var hero = s.Allies[0];
        var eff = new CardEffect("upgrade", EffectScope.Self, null, 1, Pile: "hand");
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { UnUpgradableCard() });
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), cat);
        Assert.False(next.Hand[0].IsUpgraded);
        Assert.Empty(evs);  // 強化候補がないため無発火
    }

    [Fact] public void Upgrade_clamps_to_candidate_count()
    {
        var hand = ImmutableArray.Create(
            new BattleCardInstance("c1", "strike", false, null));
        var s = MakeState(hand);
        var hero = s.Allies[0];
        var eff = new CardEffect("upgrade", EffectScope.Self, null, 5, Pile: "hand");
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { UpgradableStrike() });
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), cat);
        Assert.True(next.Hand[0].IsUpgraded);
        Assert.Equal(1, evs[0].Amount);
    }

    [Fact] public void Upgrade_empty_pile_emits_no_event()
    {
        var s = MakeState();
        var hero = s.Allies[0];
        var eff = new CardEffect("upgrade", EffectScope.Self, null, 2, Pile: "hand");
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { UpgradableStrike() });
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), cat);
        Assert.Empty(evs);
    }

    [Fact] public void Upgrade_invalid_pile_throws()
    {
        var hand = ImmutableArray.Create(new BattleCardInstance("c1", "strike", false, null));
        var s = MakeState(hand);
        var hero = s.Allies[0];
        var eff = new CardEffect("upgrade", EffectScope.Self, null, 1, Pile: "invalid");
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { UpgradableStrike() });
        Assert.Throws<System.InvalidOperationException>(() =>
            EffectApplier.Apply(s, hero, eff, Rng(), cat));
    }
}
