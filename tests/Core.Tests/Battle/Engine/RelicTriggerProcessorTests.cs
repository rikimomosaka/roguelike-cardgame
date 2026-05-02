using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class RelicTriggerProcessorTests
{
    private static FakeRng MakeRng() => new FakeRng(new int[20], System.Array.Empty<double>());

    [Fact]
    public void Fire_with_no_owned_relics_returns_state_unchanged_and_empty_events()
    {
        var state = BattleFixtures.MinimalState();
        var catalog = BattleFixtures.MinimalCatalog();

        var (after, events) = RelicTriggerProcessor.Fire(
            state, "OnTurnStart", catalog, MakeRng(), orderStart: 0);

        Assert.Equal(state.Allies, after.Allies);
        Assert.Empty(events);
    }

    [Fact]
    public void Fire_with_matching_trigger_applies_relic_effects()
    {
        var blockRelic = BattleFixtures.Relic("block_relic", "OnTurnStart",
            true, new CardEffect("block", EffectScope.Self, null, 5));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { blockRelic });
        var state = BattleFixtures.MinimalState(
            ownedRelicIds: ImmutableArray.Create("block_relic"));

        var (after, events) = RelicTriggerProcessor.Fire(
            state, "OnTurnStart", catalog, MakeRng(), orderStart: 0);

        Assert.Equal(5, after.Allies[0].Block.RawTotal);
        Assert.Single(events);
        Assert.Equal(BattleEventKind.GainBlock, events[0].Kind);
        Assert.Equal("relic:block_relic", events[0].Note);
    }

    [Fact]
    public void Fire_with_Implemented_false_relic_is_noop()
    {
        var unimpl = BattleFixtures.Relic("unimpl", "OnTurnStart",
            implemented: false,
            new CardEffect("block", EffectScope.Self, null, 5));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { unimpl });
        var state = BattleFixtures.MinimalState(
            ownedRelicIds: ImmutableArray.Create("unimpl"));

        var (after, events) = RelicTriggerProcessor.Fire(
            state, "OnTurnStart", catalog, MakeRng(), orderStart: 0);

        Assert.Equal(0, after.Allies[0].Block.RawTotal);
        Assert.Empty(events);
    }

    [Fact]
    public void Fire_with_mismatched_trigger_skips_relic()
    {
        var ts = BattleFixtures.Relic("ts", "OnTurnStart",
            true, new CardEffect("block", EffectScope.Self, null, 5));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { ts });
        var state = BattleFixtures.MinimalState(
            ownedRelicIds: ImmutableArray.Create("ts"));

        var (after, events) = RelicTriggerProcessor.Fire(
            state, "OnTurnEnd", catalog, MakeRng(), orderStart: 0);

        Assert.Equal(0, after.Allies[0].Block.RawTotal);
        Assert.Empty(events);
    }

    [Fact]
    public void Fire_unknown_relicId_in_catalog_silent_skip()
    {
        var state = BattleFixtures.MinimalState(
            ownedRelicIds: ImmutableArray.Create("unknown_id"));
        var catalog = BattleFixtures.MinimalCatalog();

        var (after, events) = RelicTriggerProcessor.Fire(
            state, "OnTurnStart", catalog, MakeRng(), orderStart: 0);

        Assert.Equal(0, after.Allies[0].Block.RawTotal);
        Assert.Empty(events);
    }

    [Fact]
    public void Fire_invokes_relics_in_owned_order()
    {
        var r1 = BattleFixtures.Relic("r1", "OnTurnStart", true,
            new CardEffect("block", EffectScope.Self, null, 3));
        var r2 = BattleFixtures.Relic("r2", "OnTurnStart", true,
            new CardEffect("block", EffectScope.Self, null, 7));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { r1, r2 });
        var state = BattleFixtures.MinimalState(
            ownedRelicIds: ImmutableArray.Create("r1", "r2"));

        var (after, events) = RelicTriggerProcessor.Fire(
            state, "OnTurnStart", catalog, MakeRng(), orderStart: 0);

        Assert.Equal(10, after.Allies[0].Block.RawTotal);
        Assert.Equal(2, events.Count);
        Assert.Equal("relic:r1", events[0].Note);
        Assert.Equal("relic:r2", events[1].Note);
    }

    [Fact]
    public void Fire_when_hero_dead_returns_noop()
    {
        var dead = BattleFixtures.Hero(hp: 0);
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(dead),
            ownedRelicIds: ImmutableArray.Create("any"));
        var catalog = BattleFixtures.MinimalCatalog();

        var (after, events) = RelicTriggerProcessor.Fire(
            state, "OnTurnStart", catalog, MakeRng(), orderStart: 0);

        Assert.Empty(events);
    }

    [Fact]
    public void FireOnEnemyDeath_attaches_deadEnemy_to_Note()
    {
        var relic = BattleFixtures.Relic("od_relic", "OnEnemyDeath", true,
            new CardEffect("block", EffectScope.Self, null, 2));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });
        var state = BattleFixtures.MinimalState(
            ownedRelicIds: ImmutableArray.Create("od_relic"));

        var (after, events) = RelicTriggerProcessor.FireOnEnemyDeath(
            state, "enemy_inst_X", catalog, MakeRng(), orderStart: 0);

        Assert.Equal(2, after.Allies[0].Block.RawTotal);
        Assert.Single(events);
        Assert.Equal("relic:od_relic;deadEnemy:enemy_inst_X", events[0].Note);
    }

    [Fact]
    public void Fire_orders_events_starting_from_orderStart()
    {
        var r = BattleFixtures.Relic("r", "OnTurnStart", true,
            new CardEffect("block", EffectScope.Self, null, 1));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { r });
        var state = BattleFixtures.MinimalState(
            ownedRelicIds: ImmutableArray.Create("r"));

        var (_, events) = RelicTriggerProcessor.Fire(
            state, "OnTurnStart", catalog, MakeRng(), orderStart: 7);

        Assert.Single(events);
        Assert.Equal(7, events[0].Order);
    }
}
