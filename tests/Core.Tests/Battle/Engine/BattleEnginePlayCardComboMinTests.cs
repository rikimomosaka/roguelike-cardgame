using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class BattleEnginePlayCardComboMinTests
{
    private static IRng Rng() => new FakeRng(new int[20], new double[0]);

    private static BattleState Make(
        ImmutableArray<BattleCardInstance> hand,
        int? lastOrigCost = null,
        int combo = 0,
        bool freePass = false,
        int energy = 10) =>
        new(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(BattleFixtures.Hero()),
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: energy, EnergyMax: 10,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: hand,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: combo,
            LastPlayedOrigCost: lastOrigCost,
            NextCardComboFreePass: freePass,
            EncounterId: "enc_test");

    private static CardDefinition WithEffects(string id, int cost, params CardEffect[] effects) =>
        new(id, id, null, CardRarity.Common, CardType.Attack,
            Cost: cost, UpgradedCost: null,
            Effects: effects, UpgradedEffects: null, Keywords: null);

    [Fact] public void ComboMin_null_always_applies()
    {
        var def = WithEffects("c", 1,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5));
        var card = new BattleCardInstance("inst1", "c", false, null);
        var s = Make(ImmutableArray.Create(card), combo: 0);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(5, next.Allies[0].AttackSingle.Sum);
    }

    [Fact] public void ComboMin_2_skipped_when_newCombo_1()
    {
        var def = WithEffects("c", 1,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5),
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5, ComboMin: 2));
        var card = new BattleCardInstance("inst1", "c", false, null);
        var s = Make(ImmutableArray.Create(card));
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(5, next.Allies[0].AttackSingle.Sum);
    }

    [Fact] public void ComboMin_2_applies_when_newCombo_2()
    {
        var def = WithEffects("c1", 1,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5),
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5, ComboMin: 2));
        var card = new BattleCardInstance("inst1", "c1", false, null);
        var s = Make(ImmutableArray.Create(card), lastOrigCost: 0, combo: 1);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(10, next.Allies[0].AttackSingle.Sum);
    }

    [Fact] public void ComboMin_3_skipped_when_newCombo_2()
    {
        var def = WithEffects("c1", 1,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 1),
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 99, ComboMin: 3));
        var card = new BattleCardInstance("inst1", "c1", false, null);
        var s = Make(ImmutableArray.Create(card), lastOrigCost: 0, combo: 1);
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(1, next.Allies[0].AttackSingle.Sum);
    }

    [Fact] public void ComboMin_1_applies_on_first_play()
    {
        var def = WithEffects("c", 1,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5, ComboMin: 1));
        var card = new BattleCardInstance("inst1", "c", false, null);
        var s = Make(ImmutableArray.Create(card));
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(5, next.Allies[0].AttackSingle.Sum);
    }

    [Fact] public void ComboMin_zero_treated_as_no_filter()
    {
        var def = WithEffects("c", 1,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5, ComboMin: 0));
        var card = new BattleCardInstance("inst1", "c", false, null);
        var s = Make(ImmutableArray.Create(card));
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next, _) = BattleEngine.PlayCard(s, 0, 0, 0, Rng(), cat);
        Assert.Equal(5, next.Allies[0].AttackSingle.Sum);
    }

    [Fact] public void ComboMin_in_upgraded_effects_evaluated()
    {
        var def = new CardDefinition("c", "c", null, CardRarity.Common, CardType.Attack,
            Cost: 1, UpgradedCost: null,
            Effects: new[] { new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 1) },
            UpgradedEffects: new[] {
                new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 7),
                new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 7, ComboMin: 2),
            },
            Keywords: null);
        var card = new BattleCardInstance("inst1", "c", IsUpgraded: true, CostOverride: null);

        var s1 = Make(ImmutableArray.Create(card));
        var cat = BattleFixtures.MinimalCatalog(cards: new[] { def });
        var (next1, _) = BattleEngine.PlayCard(s1, 0, 0, 0, Rng(), cat);
        Assert.Equal(7, next1.Allies[0].AttackSingle.Sum);

        var s2 = Make(ImmutableArray.Create(card), lastOrigCost: 0, combo: 1);
        var (next2, _) = BattleEngine.PlayCard(s2, 0, 0, 0, Rng(), cat);
        Assert.Equal(14, next2.Allies[0].AttackSingle.Sum);
    }
}
