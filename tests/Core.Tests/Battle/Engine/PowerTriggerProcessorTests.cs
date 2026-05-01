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

public class PowerTriggerProcessorTests
{
    private static FakeRng MakeRng() => new FakeRng(new int[20], System.Array.Empty<double>());

    private static CardDefinition MakePower(string id, params CardEffect[] effects) =>
        new(id, id, null, CardRarity.Common, CardType.Power,
            Cost: 1, UpgradedCost: null,
            Effects: effects, UpgradedEffects: null, Keywords: null);

    [Fact]
    public void OnTurnStart_fires_matching_effect_from_power_card()
    {
        // power_demo: Trigger=OnTurnStart で「カードを 1 枚引く」
        var powerDef = MakePower("power_demo",
            new CardEffect("draw", EffectScope.Self, null, 1, Trigger: "OnTurnStart"));

        var hero = BattleFixtures.Hero();
        var instance = new BattleCardInstance("inst1", "power_demo", IsUpgraded: false, CostOverride: null);
        var draw1 = new BattleCardInstance("d1", "strike", IsUpgraded: false, CostOverride: null);
        var state = BattleFixtures.MakeStateWithHero(hero) with
        {
            PowerCards = ImmutableArray.Create(instance),
            DrawPile = ImmutableArray.Create(draw1),
        };
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { powerDef, BattleFixtures.Strike() });

        var (after, events) = PowerTriggerProcessor.Fire(
            state, "OnTurnStart", catalog, MakeRng(), orderStart: 0);

        Assert.Single(after.Hand);
        Assert.Empty(after.DrawPile);
        Assert.Contains(events, e => e.Kind == BattleEventKind.Draw);
        Assert.Contains(events, e => e.Note != null && e.Note.Contains("power:power_demo"));
    }

    [Fact]
    public void Trigger_mismatch_does_not_fire()
    {
        var powerDef = MakePower("p",
            new CardEffect("draw", EffectScope.Self, null, 1, Trigger: "OnPlayCard"));
        var instance = new BattleCardInstance("i1", "p", false, null);
        var hero = BattleFixtures.Hero();
        var state = BattleFixtures.MakeStateWithHero(hero) with
        {
            PowerCards = ImmutableArray.Create(instance),
            DrawPile = ImmutableArray.Create(new BattleCardInstance("d1", "strike", false, null)),
        };
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { powerDef, BattleFixtures.Strike() });

        var (after, events) = PowerTriggerProcessor.Fire(
            state, "OnTurnStart", catalog, MakeRng(), orderStart: 0);

        Assert.Empty(after.Hand);  // OnTurnStart には反応せず
        Assert.Empty(events);
    }

    [Fact]
    public void Effect_without_trigger_does_not_fire_via_processor()
    {
        // 通常 effect (Trigger=null) は power trigger では発火しない
        var powerDef = MakePower("p",
            new CardEffect("draw", EffectScope.Self, null, 1));
        var instance = new BattleCardInstance("i1", "p", false, null);
        var hero = BattleFixtures.Hero();
        var state = BattleFixtures.MakeStateWithHero(hero) with
        {
            PowerCards = ImmutableArray.Create(instance),
        };
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { powerDef });

        var (after, events) = PowerTriggerProcessor.Fire(
            state, "OnTurnStart", catalog, MakeRng(), orderStart: 0);

        Assert.Empty(events);
    }

    [Fact]
    public void OnCombo_fires_when_count_meets_threshold()
    {
        // ComboMin=3 の OnCombo effect: combo=3 で発火、combo=2 は不発
        var powerDef = MakePower("combo_p",
            new CardEffect("block", EffectScope.Self, null, 4, ComboMin: 3, Trigger: "OnCombo"));
        var instance = new BattleCardInstance("i1", "combo_p", false, null);
        var hero = BattleFixtures.Hero();
        var state = BattleFixtures.MakeStateWithHero(hero) with
        {
            PowerCards = ImmutableArray.Create(instance),
        };
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { powerDef });

        var (afterMet, eventsMet) = PowerTriggerProcessor.FireOnCombo(
            state, comboCount: 3, catalog, MakeRng(), orderStart: 0);
        Assert.Equal(4, afterMet.Allies[0].Block.RawTotal);
        Assert.Single(eventsMet);
        Assert.Equal(BattleEventKind.GainBlock, eventsMet[0].Kind);
        Assert.Contains("power:combo_p", eventsMet[0].Note);

        var (afterShort, eventsShort) = PowerTriggerProcessor.FireOnCombo(
            state, comboCount: 2, catalog, MakeRng(), orderStart: 0);
        Assert.Equal(0, afterShort.Allies[0].Block.RawTotal);
        Assert.Empty(eventsShort);
    }

    [Fact]
    public void OnDamageReceived_fires_via_dedicated_entry()
    {
        var powerDef = MakePower("od_p",
            new CardEffect("block", EffectScope.Self, null, 2, Trigger: "OnDamageReceived"));
        var instance = new BattleCardInstance("i1", "od_p", false, null);
        var hero = BattleFixtures.Hero();
        var state = BattleFixtures.MakeStateWithHero(hero) with
        {
            PowerCards = ImmutableArray.Create(instance),
        };
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { powerDef });

        var (after, events) = PowerTriggerProcessor.FireOnDamageReceived(
            state, catalog, MakeRng(), orderStart: 0);

        Assert.Equal(2, after.Allies[0].Block.RawTotal);
        Assert.Single(events);
        Assert.Equal(BattleEventKind.GainBlock, events[0].Kind);
        Assert.Contains("power:od_p", events[0].Note);
    }

    [Fact]
    public void Multiple_power_cards_fire_in_order()
    {
        var p1 = MakePower("p1",
            new CardEffect("block", EffectScope.Self, null, 3, Trigger: "OnTurnStart"));
        var p2 = MakePower("p2",
            new CardEffect("block", EffectScope.Self, null, 7, Trigger: "OnTurnStart"));
        var i1 = new BattleCardInstance("i1", "p1", false, null);
        var i2 = new BattleCardInstance("i2", "p2", false, null);
        var hero = BattleFixtures.Hero();
        var state = BattleFixtures.MakeStateWithHero(hero) with
        {
            PowerCards = ImmutableArray.Create(i1, i2),
        };
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { p1, p2 });

        var (after, events) = PowerTriggerProcessor.Fire(
            state, "OnTurnStart", catalog, MakeRng(), orderStart: 0);

        Assert.Equal(10, after.Allies[0].Block.RawTotal);
        Assert.Equal(2, events.Count);
        Assert.Contains("power:p1", events[0].Note);
        Assert.Contains("power:p2", events[1].Note);
    }

    [Fact]
    public void Hero_dead_skips_subsequent_powers()
    {
        var dead = BattleFixtures.Hero(hp: 0);
        var powerDef = MakePower("p",
            new CardEffect("block", EffectScope.Self, null, 5, Trigger: "OnTurnStart"));
        var instance = new BattleCardInstance("i1", "p", false, null);
        var state = BattleFixtures.MakeStateWithHero(dead) with
        {
            PowerCards = ImmutableArray.Create(instance),
        };
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { powerDef });

        var (after, events) = PowerTriggerProcessor.Fire(
            state, "OnTurnStart", catalog, MakeRng(), orderStart: 0);

        Assert.Empty(events);
    }

    [Fact]
    public void Fire_orders_events_starting_from_orderStart()
    {
        var powerDef = MakePower("p",
            new CardEffect("block", EffectScope.Self, null, 1, Trigger: "OnTurnStart"));
        var instance = new BattleCardInstance("i1", "p", false, null);
        var hero = BattleFixtures.Hero();
        var state = BattleFixtures.MakeStateWithHero(hero) with
        {
            PowerCards = ImmutableArray.Create(instance),
        };
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { powerDef });

        var (_, events) = PowerTriggerProcessor.Fire(
            state, "OnTurnStart", catalog, MakeRng(), orderStart: 7);

        Assert.Single(events);
        Assert.Equal(7, events[0].Order);
    }

    [Fact]
    public void Upgraded_power_uses_UpgradedEffects_when_available()
    {
        var powerDef = new CardDefinition(
            Id: "up_p", Name: "up_p", DisplayName: null,
            Rarity: CardRarity.Common, CardType: CardType.Power,
            Cost: 1, UpgradedCost: null,
            Effects: new CardEffect[] {
                new("block", EffectScope.Self, null, 1, Trigger: "OnTurnStart"),
            },
            UpgradedEffects: new CardEffect[] {
                new("block", EffectScope.Self, null, 9, Trigger: "OnTurnStart"),
            },
            Keywords: null);
        var instance = new BattleCardInstance("i1", "up_p", IsUpgraded: true, CostOverride: null);
        var hero = BattleFixtures.Hero();
        var state = BattleFixtures.MakeStateWithHero(hero) with
        {
            PowerCards = ImmutableArray.Create(instance),
        };
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { powerDef });

        var (after, _) = PowerTriggerProcessor.Fire(
            state, "OnTurnStart", catalog, MakeRng(), orderStart: 0);

        Assert.Equal(9, after.Allies[0].Block.RawTotal);
    }
}
