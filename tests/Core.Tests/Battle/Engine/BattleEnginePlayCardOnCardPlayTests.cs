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

public class BattleEnginePlayCardOnCardPlayTests
{
    private static FakeRng MakeRng() => new FakeRng(new int[20], System.Array.Empty<double>());

    [Fact]
    public void OnCardPlay_fires_after_card_effects_before_card_movement()
    {
        var relic = BattleFixtures.Relic("oc", "OnPlayCard", true,
            new CardEffect("block", EffectScope.Self, null, 3));
        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { BattleFixtures.Strike() },
            relics: new[] { relic });

        var card = BattleFixtures.MakeBattleCard("strike", "c1");
        var state = BattleFixtures.MinimalState(
            hand: ImmutableArray.Create(card),
            ownedRelicIds: ImmutableArray.Create("oc")) with { Energy = 1 };

        var (after, events) = BattleEngine.PlayCard(state, 0, 0, 0, MakeRng(), catalog);

        // strike effect (attack 6 → AttackSingle), then OnCardPlay relic block 3
        Assert.Equal(6, after.Allies[0].AttackSingle.Sum);
        Assert.Equal(3, after.Allies[0].Block.RawTotal);
        // events: PlayCard → AttackPool 加算は event なし → relic GainBlock with relic:oc
        var relicEv = events.FirstOrDefault(e =>
            e.Kind == BattleEventKind.GainBlock && e.Note != null && e.Note.Contains("relic:oc"));
        Assert.NotNull(relicEv);
        // カード移動: Discard へ (strike は exhaustSelf/retainSelf/Power/Unit でない)
        Assert.Single(after.DiscardPile);
        Assert.Equal("c1", after.DiscardPile[0].InstanceId);
    }

    [Fact]
    public void OnCardPlay_with_no_relics_keeps_existing_behavior()
    {
        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { BattleFixtures.Strike() });

        var card = BattleFixtures.MakeBattleCard("strike", "c1");
        var state = BattleFixtures.MinimalState(
            hand: ImmutableArray.Create(card)) with { Energy = 1 };

        var (after, _) = BattleEngine.PlayCard(state, 0, 0, 0, MakeRng(), catalog);

        Assert.Equal(6, after.Allies[0].AttackSingle.Sum);
        Assert.Single(after.DiscardPile);
    }

    [Fact]
    public void OnCardPlay_relic_summon_does_not_affect_card_self_summonSucceeded()
    {
        // strike (Attack カード, NonUnit) をプレイし、OnCardPlay レリックが summon 効果を持つ
        // → カード自身は Discard へ移動 (Unit でないので SummonHeld には行かない)
        var relic = BattleFixtures.Relic("summon_r", "OnPlayCard", true,
            new CardEffect("summon", EffectScope.Self, null, 0, UnitId: "minion"));
        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { BattleFixtures.Strike() },
            relics: new[] { relic });

        var card = BattleFixtures.MakeBattleCard("strike", "c1");
        var state = BattleFixtures.MinimalState(
            hand: ImmutableArray.Create(card),
            ownedRelicIds: ImmutableArray.Create("summon_r")) with { Energy = 1 };

        var (after, _) = BattleEngine.PlayCard(state, 0, 0, 0, MakeRng(), catalog);

        // Allies に minion が追加される
        Assert.Equal(2, after.Allies.Length);
        // strike カード自身は Discard へ (summon 成功フラグはカード自身の effect ループ内でのみセット)
        Assert.Single(after.DiscardPile);
        Assert.Empty(after.SummonHeld);
    }

    [Fact]
    public void Unit_card_binds_its_direct_summon_not_the_later_OnPlayCard_relic_summon()
    {
        var unitCard = new CardDefinition(
            "summon_direct", "Summon Direct", null,
            CardRarity.Common, CardType.Unit,
            Cost: 1, UpgradedCost: null,
            Effects:
            [
                new CardEffect(
                    "summon", EffectScope.Self, null, 0,
                    UnitId: "direct_minion"),
            ],
            UpgradedEffects: null, Keywords: null);
        var relic = BattleFixtures.Relic(
            "summon_relic", "OnPlayCard", true,
            new CardEffect(
                "summon", EffectScope.Self, null, 0,
                UnitId: "relic_minion"));
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [unitCard],
            units:
            [
                BattleFixtures.MinionDef("direct_minion"),
                BattleFixtures.MinionDef("relic_minion"),
            ],
            relics: [relic]);
        var card = BattleFixtures.MakeBattleCard(unitCard.Id, "unit-card-1");
        var state = BattleFixtures.MinimalState(
            hand: [card],
            ownedRelicIds: [relic.Id]) with { Energy = 1 };
        var rng = new FakeRng(
            [0x1111, 0x2222], System.Array.Empty<double>());

        var (after, _) = BattleEngine.PlayCard(
            state, handIndex: 0, targetEnemyIndex: 0,
            targetAllyIndex: 0, rng, catalog);

        Assert.Equal("unit-card-1", Assert.Single(after.SummonHeld).InstanceId);
        var direct = Assert.Single(
            after.Allies, actor => actor.DefinitionId == "direct_minion");
        var relicSummon = Assert.Single(
            after.Allies, actor => actor.DefinitionId == "relic_minion");
        Assert.Equal("unit-card-1", direct.AssociatedSummonHeldInstanceId);
        Assert.Null(relicSummon.AssociatedSummonHeldInstanceId);
    }

    [Fact]
    public void Secondary_participant_keeps_direct_Unit_summon_ownership_across_cleanup()
    {
        var unitCard = new CardDefinition(
            "secondary_summon", "Secondary Summon", null,
            CardRarity.Common, CardType.Unit,
            Cost: 1, UpgradedCost: null,
            Effects:
            [
                new CardEffect(
                    "summon", EffectScope.Self, null, 0,
                    UnitId: "direct_minion"),
            ],
            UpgradedEffects: null, Keywords: null);
        var relic = BattleFixtures.Relic(
            "secondary_summon_relic", "OnPlayCard", true,
            new CardEffect(
                "summon", EffectScope.Self, null, 0,
                UnitId: "relic_minion"));
        var owner = BattleFixtures.SummonActor(
            "secondary-owner", "secondary-character", 1, hp: 30);
        var state = BattleFixtures.MinimalState(
            ownedRelicIds: ImmutableArray<string>.Empty) with
        {
            Allies = [BattleFixtures.Hero(), owner],
        };
        state = BattleParticipantAccess.InitializePrimary(state);
        var secondary = state.Participants[0] with
        {
            ParticipantId = "player:secondary",
            ActorInstanceId = owner.InstanceId,
            ControllerKind = BattleParticipantControllerKind.Human,
            OwnerPlayerSlot = owner.SlotIndex,
            CharacterId = owner.DefinitionId,
            Energy = 1,
            Hand =
            [
                BattleFixtures.MakeBattleCard(
                    unitCard.Id, "secondary-unit-card"),
            ],
            DrawPile = ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile = ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile = ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld = ImmutableArray<BattleCardInstance>.Empty,
            PowerCards = ImmutableArray<BattleCardInstance>.Empty,
            OwnedRelicIds = [relic.Id],
        };
        state = BattleParticipantAccess.Add(state, secondary);
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [unitCard],
            units:
            [
                BattleFixtures.MinionDef("direct_minion"),
                BattleFixtures.MinionDef("relic_minion"),
            ],
            relics: [relic]);

        var (summoned, _) = BattleEngine.PlayCard(
            state, secondary.ParticipantId, handIndex: 0,
            targetEnemyIndex: 0, targetAllyIndex: 1,
            new FakeRng([0x1111, 0x2222], System.Array.Empty<double>()),
            catalog);

        var direct = Assert.Single(
            summoned.Allies,
            actor => actor.DefinitionId == "direct_minion");
        Assert.Equal(
            "secondary-unit-card",
            direct.AssociatedSummonHeldInstanceId);
        Assert.Null(Assert.Single(
            summoned.Allies,
            actor => actor.DefinitionId == "relic_minion")
            .AssociatedSummonHeldInstanceId);
        var afterPlay = summoned.Participants.Single(participant =>
            participant.ParticipantId == secondary.ParticipantId);
        Assert.Equal(
            "secondary-unit-card",
            Assert.Single(afterPlay.SummonHeld).InstanceId);
        Assert.Empty(summoned.SummonHeld);

        var dead = summoned with
        {
            Allies = summoned.Allies
                .Select(actor => actor.InstanceId == direct.InstanceId
                    ? actor with { CurrentHp = 0 }
                    : actor)
                .ToImmutableArray(),
        };
        var events = new System.Collections.Generic.List<BattleEvent>();
        int order = 0;
        var cleaned = SummonCleanup.Apply(dead, events, ref order);
        var afterCleanup = cleaned.Participants.Single(participant =>
            participant.ParticipantId == secondary.ParticipantId);

        Assert.Empty(afterCleanup.SummonHeld);
        Assert.Equal(
            "secondary-unit-card",
            Assert.Single(afterCleanup.DiscardPile).InstanceId);
        Assert.Empty(cleaned.DiscardPile);
    }

    [Fact]
    public void OnPlayCard_relic_random_cost_change_excludes_the_playing_card()
    {
        var relic = BattleFixtures.Relic(
            "cost_relic", "OnPlayCard", true,
            new CardEffect(
                "modifyCardCost", EffectScope.Self, Side: null, Amount: -1,
                Pile: "hand", Select: "random",
                CostDuration: "untilPlayed", CardCount: 1));
        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { BattleFixtures.Strike(), BattleFixtures.Defend() },
            relics: new[] { relic });
        var playing = BattleFixtures.MakeBattleCard("strike", "playing");
        var remaining = BattleFixtures.MakeBattleCard("defend", "remaining");
        var state = BattleFixtures.MinimalState(
            hand: ImmutableArray.Create(playing, remaining),
            ownedRelicIds: ImmutableArray.Create(relic.Id)) with { Energy = 1 };

        // With the unfiltered two-card hand, RNG index 0 would select the
        // in-flight card. The resolution context must leave only "remaining"
        // eligible before the relic consumes this value.
        var rng = new FakeRng(new[] { 0 }, System.Array.Empty<double>());
        var (after, events) = BattleEngine.PlayCard(
            state, handIndex: 0, targetEnemyIndex: 0, targetAllyIndex: 0,
            rng, catalog);

        var card = Assert.Single(after.Hand);
        Assert.Equal("remaining", card.InstanceId);
        Assert.Equal(-1, Assert.Single(card.EffectiveCostModifiers).Amount);
        var modify = Assert.Single(
            events, e => e.Kind == BattleEventKind.ModifyCardCost);
        Assert.Equal("remaining", modify.TargetInstanceId);
        Assert.Equal(CombatSourceKind.RelicEffect, modify.Source?.Kind);
    }

    [Fact]
    public void Nested_OnCardDiscarded_relic_keeps_the_playing_card_excluded()
    {
        var discardCard = new CardDefinition(
            "discard_skill", "Discard", null,
            CardRarity.Common, CardType.Skill,
            Cost: 0, UpgradedCost: null,
            Effects: new[]
            {
                new CardEffect(
                    "discard", EffectScope.Random, Side: null, Amount: 1,
                    Select: "random"),
            },
            UpgradedEffects: null, Keywords: null);
        var relic = BattleFixtures.Relic(
            "discard_cost_relic", "OnCardDiscarded", true,
            new CardEffect(
                "modifyCardCost", EffectScope.Self, Side: null, Amount: -1,
                Pile: "hand", Select: "random",
                CostDuration: "untilPlayed", CardCount: 1));
        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[]
            {
                discardCard, BattleFixtures.Strike(), BattleFixtures.Defend(),
            },
            relics: new[] { relic });
        var playing = BattleFixtures.MakeBattleCard(discardCard.Id, "playing");
        var discarded = BattleFixtures.MakeBattleCard("strike", "discarded");
        var remaining = BattleFixtures.MakeBattleCard("defend", "remaining");
        var state = BattleFixtures.MinimalState(
            hand: ImmutableArray.Create(playing, discarded, remaining),
            ownedRelicIds: ImmutableArray.Create(relic.Id));

        // First 0 discards "discarded" from the eligible non-playing cards.
        // Second 0 would point at "playing" if the nested trigger lost context.
        var rng = new FakeRng(new[] { 0, 0 }, System.Array.Empty<double>());
        var (after, events) = BattleEngine.PlayCard(
            state, handIndex: 0, targetEnemyIndex: null,
            targetAllyIndex: null, rng, catalog);

        var card = Assert.Single(after.Hand);
        Assert.Equal("remaining", card.InstanceId);
        Assert.Equal(-1, Assert.Single(card.EffectiveCostModifiers).Amount);
        Assert.Equal(
            "remaining",
            Assert.Single(events, e => e.Kind == BattleEventKind.ModifyCardCost)
                .TargetInstanceId);
        Assert.Equal(
            new[] { "discarded", "playing" },
            after.DiscardPile.Select(c => c.InstanceId));
    }
}
