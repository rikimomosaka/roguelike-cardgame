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

/// <summary>
/// Phase 10.5.L1.5: discard / exhaust 末尾で OnCardDiscarded / OnCardExhausted relic + power
/// が発火することを確認する。
/// </summary>
public class EffectApplierOnCardDiscardedExhaustedTests
{
    private static IRng MakeRng() => new FakeRng(new int[10], new double[0]);

    private static BattleState MakeState(
        ImmutableArray<BattleCardInstance>? hand = null,
        ImmutableArray<BattleCardInstance>? draw = null,
        ImmutableArray<string>? ownedRelicIds = null) =>
        BattleFixtures.MinimalState(
            hand: hand,
            draw: draw,
            ownedRelicIds: ownedRelicIds);

    [Fact]
    public void Discard_random_fires_OnCardDiscarded_relic()
    {
        // Relic with effect[trigger=OnCardDiscarded, action=block self 4]
        var relic = BattleFixtures.Relic("oc_relic", "OnCardDiscarded", true,
            new CardEffect("block", EffectScope.Self, null, 4));
        var hand = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "c1"));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });
        var state = MakeState(hand: hand,
            ownedRelicIds: ImmutableArray.Create("oc_relic"));

        var hero = state.Allies[0];
        var eff = new CardEffect("discard", EffectScope.Random, null, 1);
        var (after, evs) = EffectApplier.Apply(state, hero, eff, MakeRng(), catalog);

        // discard 1 → relic fires GainBlock 4
        Assert.Single(after.DiscardPile);
        Assert.Equal(4, after.Allies[0].Block.RawTotal);
        var relicEv = evs.FirstOrDefault(e =>
            e.Kind == BattleEventKind.GainBlock && (e.Note?.Contains("relic:oc_relic") ?? false));
        Assert.NotNull(relicEv);
    }

    [Fact]
    public void Discard_zero_does_not_fire_OnCardDiscarded()
    {
        var relic = BattleFixtures.Relic("oc_relic", "OnCardDiscarded", true,
            new CardEffect("block", EffectScope.Self, null, 4));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });
        var state = MakeState(
            hand: ImmutableArray<BattleCardInstance>.Empty,
            ownedRelicIds: ImmutableArray.Create("oc_relic"));

        var hero = state.Allies[0];
        var eff = new CardEffect("discard", EffectScope.Random, null, 1);
        var (after, evs) = EffectApplier.Apply(state, hero, eff, MakeRng(), catalog);

        // empty hand → no discard → no relic fire
        Assert.Equal(0, after.Allies[0].Block.RawTotal);
        Assert.DoesNotContain(evs, e => e.Note?.Contains("relic:oc_relic") ?? false);
    }

    [Fact]
    public void ExhaustCard_fires_OnCardExhausted_relic()
    {
        var relic = BattleFixtures.Relic("oe_relic", "OnCardExhausted", true,
            new CardEffect("block", EffectScope.Self, null, 3));
        var hand = ImmutableArray.Create(
            BattleFixtures.MakeBattleCard("strike", "c1"));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });
        var state = MakeState(hand: hand,
            ownedRelicIds: ImmutableArray.Create("oe_relic"));

        var hero = state.Allies[0];
        var eff = new CardEffect("exhaustCard", EffectScope.Self, null, 1, Pile: "hand");
        var (after, evs) = EffectApplier.Apply(state, hero, eff, MakeRng(), catalog);

        // exhausted 1 → relic fires GainBlock 3
        Assert.Single(after.ExhaustPile);
        Assert.Equal(3, after.Allies[0].Block.RawTotal);
        var relicEv = evs.FirstOrDefault(e =>
            e.Kind == BattleEventKind.GainBlock && (e.Note?.Contains("relic:oe_relic") ?? false));
        Assert.NotNull(relicEv);
    }

    [Fact]
    public void ExhaustCard_zero_does_not_fire_OnCardExhausted()
    {
        var relic = BattleFixtures.Relic("oe_relic", "OnCardExhausted", true,
            new CardEffect("block", EffectScope.Self, null, 3));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });
        var state = MakeState(
            hand: ImmutableArray<BattleCardInstance>.Empty,
            ownedRelicIds: ImmutableArray.Create("oe_relic"));

        var hero = state.Allies[0];
        var eff = new CardEffect("exhaustCard", EffectScope.Self, null, 1, Pile: "hand");
        var (after, evs) = EffectApplier.Apply(state, hero, eff, MakeRng(), catalog);

        Assert.Equal(0, after.Allies[0].Block.RawTotal);
        Assert.DoesNotContain(evs, e => e.Note?.Contains("relic:oe_relic") ?? false);
    }

    [Fact]
    public void ExhaustSelf_fires_OnCardExhausted_relic()
    {
        var relic = BattleFixtures.Relic("oe_relic", "OnCardExhausted", true,
            new CardEffect("block", EffectScope.Self, null, 2));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });
        var state = MakeState(ownedRelicIds: ImmutableArray.Create("oe_relic"));

        var hero = state.Allies[0];
        var eff = new CardEffect("exhaustSelf", EffectScope.Self, null, 0);
        var (after, evs) = EffectApplier.Apply(state, hero, eff, MakeRng(), catalog);

        // exhaustSelf event + relic fire
        Assert.Equal(2, after.Allies[0].Block.RawTotal);
        var relicEv = evs.FirstOrDefault(e =>
            e.Kind == BattleEventKind.GainBlock && (e.Note?.Contains("relic:oe_relic") ?? false));
        Assert.NotNull(relicEv);
    }

    [Fact]
    public void Discard_fires_OnCardDiscarded_power()
    {
        // Power card with effect[trigger=OnCardDiscarded, action=block self 5]
        var pwrCardDef = new CardDefinition(
            Id: "pwr_oc",
            Name: "pwr_oc",
            DisplayName: null,
            Rarity: CardRarity.Common,
            CardType: CardType.Power,
            Cost: 1,
            UpgradedCost: null,
            Effects: new[]
            {
                new CardEffect("block", EffectScope.Self, null, 5, Trigger: "OnCardDiscarded"),
            },
            UpgradedEffects: null,
            Keywords: null);
        var pwrInstance = BattleFixtures.MakeBattleCard("pwr_oc", "p1");
        var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "c1"));

        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { BattleFixtures.Strike(), BattleFixtures.Defend(), pwrCardDef });

        var state = MakeState(hand: hand) with
        {
            PowerCards = ImmutableArray.Create(pwrInstance),
        };

        var hero = state.Allies[0];
        var eff = new CardEffect("discard", EffectScope.Random, null, 1);
        var (after, evs) = EffectApplier.Apply(state, hero, eff, MakeRng(), catalog);

        Assert.Equal(5, after.Allies[0].Block.RawTotal);
        var pwrEv = evs.FirstOrDefault(e =>
            e.Kind == BattleEventKind.GainBlock && (e.Note?.Contains("power:pwr_oc") ?? false));
        Assert.NotNull(pwrEv);
    }
}
