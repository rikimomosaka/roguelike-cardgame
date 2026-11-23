using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public sealed class GeneratedCardPoolInheritanceTests
{
    [Fact]
    public void Effective_pool_prefers_instance_override_then_definition_then_universal()
    {
        var guardianDefinition = Card(
            "pool-card", CardType.Unit, CardPoolClass.Guardian);
        var overridden = Instance(
            guardianDefinition, "pool-card-1", CardPoolClass.Striker);
        var ordinary = Instance(guardianDefinition, "pool-card-2");
        var unknown = new BattleCardInstance(
            "missing-1", "missing-definition", false, null);

        Assert.Equal(
            CardPoolClass.Striker,
            overridden.EffectivePoolClass(guardianDefinition));
        Assert.Equal(
            CardPoolClass.Guardian,
            ordinary.EffectivePoolClass(guardianDefinition));
        Assert.Equal(
            CardPoolClass.Universal,
            unknown.EffectivePoolClass(definition: null));
    }

    [Fact]
    public void Pool_override_participates_in_instance_and_snapshot_equality_and_hashing()
    {
        var striker = new BattleCardInstance(
            "same", "token", false, null)
        {
            PoolClassOverride = CardPoolClass.Striker,
        };
        var strikerWithEmptyModifiers = striker with
        {
            CostModifiers = ImmutableArray<CardCostModifier>.Empty,
        };
        var guardian = striker with
        {
            PoolClassOverride = CardPoolClass.Guardian,
        };

        Assert.Equal(striker, strikerWithEmptyModifiers);
        Assert.NotEqual(striker, guardian);
        Assert.Equal(2, new HashSet<BattleCardInstance>
        {
            striker,
            strikerWithEmptyModifiers,
            guardian,
        }.Count);

        var baseSnapshot = Snapshot(CardPoolClass.Striker);
        var otherSnapshot = baseSnapshot with
        {
            PoolClass = CardPoolClass.Guardian,
        };
        Assert.NotEqual(baseSnapshot, otherSnapshot);
        Assert.Equal(2, new HashSet<PlayedCardEffectSnapshot>
        {
            baseSnapshot,
            otherSnapshot,
        }.Count);
    }

    [Theory]
    [InlineData("hand")]
    [InlineData("draw")]
    [InlineData("discard")]
    [InlineData("exhaust")]
    [InlineData("summonHeld")]
    [InlineData("power")]
    public void AddCard_finds_the_source_override_in_every_card_zone(string zone)
    {
        var sourceDefinition = Card(
            "zone-source", CardType.Skill, CardPoolClass.Guardian);
        var tokenDefinition = Card(
            "shared-token", CardType.Attack, CardPoolClass.Enhancer);
        var sourceCard = Instance(
            sourceDefinition, $"zone-source-{zone}", CardPoolClass.Striker);
        var state = PutInZone(BattleFixtures.MinimalState(), sourceCard, zone);
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [sourceDefinition, tokenDefinition]);
        var source = CardSource(
            sourceCard,
            sourceDefinition,
            zone == "power"
                ? CombatSourceKind.PowerEffect
                : CombatSourceKind.CardEffect);

        var (next, _) = EffectApplier.Apply(
            state,
            state.Allies[0],
            AddCard(tokenDefinition.Id),
            Rng(),
            catalog,
            source);

        var generated = Assert.Single(next.DiscardPile, card =>
            card.CardDefinitionId == tokenDefinition.Id);
        Assert.Equal(CardPoolClass.Striker, generated.PoolClassOverride);
        Assert.Equal(
            CardPoolClass.Striker,
            generated.EffectivePoolClass(tokenDefinition));
    }

    [Theory]
    [InlineData(CardPoolClass.Striker)]
    [InlineData(CardPoolClass.Guardian)]
    public void The_same_token_definition_inherits_each_generating_card_pool(
        CardPoolClass sourcePool)
    {
        var sourceDefinition = Card(
            "creator", CardType.Skill, CardPoolClass.Universal);
        var tokenDefinition = Card(
            "one-token", CardType.Skill, CardPoolClass.Enhancer);
        var sourceCard = Instance(sourceDefinition, "creator-1", sourcePool);
        var state = BattleFixtures.MinimalState(hand: [sourceCard]);
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [sourceDefinition, tokenDefinition]);

        var (next, _) = EffectApplier.Apply(
            state,
            state.Allies[0],
            AddCard(tokenDefinition.Id),
            Rng(),
            catalog,
            CardSource(sourceCard, sourceDefinition));

        Assert.Equal(
            sourcePool,
            Assert.Single(next.DiscardPile).PoolClassOverride);
    }

    [Fact]
    public void Unknown_source_explicitly_overrides_generated_card_to_universal()
    {
        var tokenDefinition = Card(
            "unknown-source-token", CardType.Skill, CardPoolClass.Enhancer);
        var state = BattleFixtures.MinimalState();
        var catalog = BattleFixtures.MinimalCatalog(cards: [tokenDefinition]);
        var missingSource = new CombatSource(
            CombatSourceKind.CardEffect,
            OwnerSlot: 0,
            OwnerInstanceId: state.Allies[0].InstanceId,
            ActorInstanceId: state.Allies[0].InstanceId,
            DefinitionId: "missing-source",
            InstanceId: "missing-source-instance");

        var (next, _) = EffectApplier.Apply(
            state,
            state.Allies[0],
            AddCard(tokenDefinition.Id),
            Rng(),
            catalog,
            missingSource);

        var generated = Assert.Single(next.DiscardPile);
        Assert.Equal(CardPoolClass.Universal, generated.PoolClassOverride);
        Assert.Equal(
            CardPoolClass.Universal,
            generated.EffectivePoolClass(tokenDefinition));
    }

    [Fact]
    public void Card_source_definition_is_used_when_its_instance_is_no_longer_present()
    {
        var sourceDefinition = Card(
            "definition-only-source", CardType.Skill, CardPoolClass.Tactician);
        var tokenDefinition = Card(
            "definition-only-token", CardType.Skill, CardPoolClass.Enhancer);
        var state = BattleFixtures.MinimalState();
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [sourceDefinition, tokenDefinition]);
        var absentSource = new CombatSource(
            CombatSourceKind.CardEffect,
            OwnerSlot: 0,
            OwnerInstanceId: state.Allies[0].InstanceId,
            ActorInstanceId: state.Allies[0].InstanceId,
            DefinitionId: sourceDefinition.Id,
            InstanceId: "absent-source-instance");

        var (next, _) = EffectApplier.Apply(
            state,
            state.Allies[0],
            AddCard(tokenDefinition.Id),
            Rng(),
            catalog,
            absentSource);

        Assert.Equal(
            CardPoolClass.Tactician,
            Assert.Single(next.DiscardPile).PoolClassOverride);
    }

    [Fact]
    public void Universal_source_explicitly_overrides_a_class_token_definition()
    {
        var sourceDefinition = Card(
            "universal-source", CardType.Skill, CardPoolClass.Universal);
        var tokenDefinition = Card(
            "universal-source-token", CardType.Skill, CardPoolClass.Enhancer);
        var sourceCard = Instance(sourceDefinition, "universal-source-1");
        var state = BattleFixtures.MinimalState(hand: [sourceCard]);
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [sourceDefinition, tokenDefinition]);

        var (next, _) = EffectApplier.Apply(
            state,
            state.Allies[0],
            AddCard(tokenDefinition.Id),
            Rng(),
            catalog,
            CardSource(sourceCard, sourceDefinition));

        var generated = Assert.Single(next.DiscardPile);
        Assert.Equal(CardPoolClass.Universal, generated.PoolClassOverride);
        Assert.Equal(
            CardPoolClass.Universal,
            generated.EffectivePoolClass(tokenDefinition));
    }

    [Fact]
    public void Actual_play_snapshots_and_generated_cards_use_the_instance_pool()
    {
        var tokenDefinition = Card(
            "play-token", CardType.Skill, CardPoolClass.Universal);
        var sourceDefinition = Card(
            "play-source",
            CardType.Skill,
            CardPoolClass.Guardian,
            effects: [AddCard(tokenDefinition.Id)]);
        var sourceCard = Instance(
            sourceDefinition, "play-source-1", CardPoolClass.Striker);
        var state = BattleFixtures.MinimalState(hand: [sourceCard]);
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [sourceDefinition, tokenDefinition]);

        var (next, _) = BattleEngine.PlayCard(
            state, 0, 0, 0, Rng(), catalog);

        Assert.Equal(
            CardPoolClass.Striker,
            Assert.Single(next.PlayedCardHistoryThisTurn).PoolClass);
        Assert.Equal(
            CardPoolClass.Striker,
            Assert.Single(next.RepriseEligibleHistory).PoolClass);
        Assert.Equal(
            CardPoolClass.Striker,
            Assert.Single(next.DiscardPile, card =>
                card.CardDefinitionId == tokenDefinition.Id).PoolClassOverride);
    }

    [Fact]
    public void Triggered_power_effect_inherits_the_power_card_instance_pool()
    {
        var tokenDefinition = Card(
            "power-token", CardType.Skill, CardPoolClass.Universal);
        var powerDefinition = Card(
            "token-power",
            CardType.Power,
            CardPoolClass.Guardian,
            effects:
            [
                AddCard(tokenDefinition.Id) with { Trigger = "OnTurnStart" },
            ]);
        var powerCard = Instance(
            powerDefinition, "token-power-1", CardPoolClass.Striker);
        var state = BattleFixtures.MinimalState() with
        {
            PowerCards = [powerCard],
        };
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [powerDefinition, tokenDefinition]);

        var (next, _) = PowerTriggerProcessor.Fire(
            state, "OnTurnStart", catalog, Rng(), orderStart: 0);

        Assert.Equal(
            CardPoolClass.Striker,
            Assert.Single(next.DiscardPile).PoolClassOverride);
    }

    [Fact]
    public void Reprise_uses_snapshot_pool_after_the_original_card_leaves_all_zones()
    {
        var tokenDefinition = Card(
            "reprise-token", CardType.Skill, CardPoolClass.Enhancer);
        var sourceDefinition = Card(
            "reprise-source",
            CardType.Skill,
            CardPoolClass.Guardian,
            effects: [AddCard(tokenDefinition.Id)]);
        var repriseDefinition = Card(
            "reprise-card",
            CardType.Skill,
            CardPoolClass.Tactician,
            abilities:
            [
                new RepriseAbilityDefinition(
                    new RepriseCardFilter(
                        CardTypes: [CardType.Skill],
                        MaxOriginalCost: 0)),
            ]);
        var sourceCard = Instance(
            sourceDefinition, "reprise-source-1", CardPoolClass.Striker);
        var repriseCard = Instance(repriseDefinition, "reprise-card-1");
        var state = BattleFixtures.MinimalState(
            hand: [sourceCard, repriseCard]);
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [sourceDefinition, repriseDefinition, tokenDefinition]);

        var (afterSource, _) = BattleEngine.PlayCard(
            state, 0, 0, 0, Rng(), catalog);
        afterSource = afterSource with
        {
            DiscardPile = afterSource.DiscardPile
                .Where(card => card.InstanceId != sourceCard.InstanceId)
                .ToImmutableArray(),
        };
        var (afterReprise, _) = BattleEngine.PlayCard(
            afterSource, 0, 0, 0, Rng(), catalog);

        var generated = afterReprise.DiscardPile
            .Where(card => card.CardDefinitionId == tokenDefinition.Id)
            .ToArray();
        Assert.Equal(2, generated.Length);
        Assert.All(generated, card =>
            Assert.Equal(CardPoolClass.Striker, card.PoolClassOverride));
    }

    [Fact]
    public void Afterimage_uses_snapshot_pool_without_an_original_card_instance()
    {
        var tokenDefinition = Card(
            "afterimage-token", CardType.Skill, CardPoolClass.Striker);
        var hero = BattleFixtures.WithStatus(
            BattleFixtures.Hero(), BattleStatusIds.Afterimage, 1);
        var snapshot = Snapshot(
            CardPoolClass.Guardian,
            cardDefinitionId: "absent-afterimage-source",
            cardInstanceId: "absent-afterimage-source-1",
            effects: [AddCard(tokenDefinition.Id)]);
        var state = BattleFixtures.MinimalState(allies: [hero]) with
        {
            Phase = BattlePhase.PlayerAttacking,
            LastFinisherRecord = [snapshot],
        };
        var catalog = BattleFixtures.MinimalCatalog(cards: [tokenDefinition]);

        var result = BattleEngine.ResolveAfterimageAtActorEnd(
            state, hero.InstanceId, Rng(), catalog, orderStart: 0);

        Assert.False(result.Paused);
        Assert.Equal(
            CardPoolClass.Guardian,
            Assert.Single(result.State.DiscardPile).PoolClassOverride);
    }

    [Fact]
    public void Contingency_snapshot_and_generated_card_keep_the_armed_instance_pool()
    {
        var tokenDefinition = Card(
            "contingency-token", CardType.Skill, CardPoolClass.Enhancer);
        var planDefinition = Card(
            "contingency-source",
            CardType.Skill,
            CardPoolClass.Striker,
            effects: [AddCard(tokenDefinition.Id)],
            abilities:
            [
                new ContingencyAbilityDefinition(
                    new ContingencyTriggerDefinition(
                        ContingencyTriggerTiming.TurnStart)),
            ]);
        var planCard = Instance(
            planDefinition, "contingency-source-1", CardPoolClass.Guardian);
        var state = BattleFixtures.MinimalState() with
        {
            ExhaustPile = [planCard],
            ArmedContingencies =
            [
                new ArmedContingency(
                    planCard.InstanceId,
                    "hero_inst",
                    new ContingencyTriggerDefinition(
                        ContingencyTriggerTiming.TurnStart),
                    ArmedOrdinal: 1),
            ],
        };
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [planDefinition, tokenDefinition]);

        var result = ContingencyResolutionEngine.ResolveTurnStart(
            state, Rng(), catalog);

        Assert.Equal(
            CardPoolClass.Guardian,
            Assert.Single(result.State.DiscardPile, card =>
                card.CardDefinitionId == tokenDefinition.Id).PoolClassOverride);
        var triggered = Assert.Single(result.Events, battleEvent =>
            battleEvent.Kind == BattleEventKind.ContingencyTriggered);
        Assert.Equal(CardPoolClass.Guardian, triggered.ReenactedCard!.PoolClass);
    }

    [Fact]
    public void Generated_unit_keeps_its_override_while_held_and_after_cleanup()
    {
        var sourceDefinition = Card(
            "unit-creator", CardType.Skill, CardPoolClass.Striker);
        var unitDefinition = Card(
            "generated-unit", CardType.Unit, CardPoolClass.Universal);
        var sourceCard = Instance(sourceDefinition, "unit-creator-1");
        var initial = BattleFixtures.MinimalState(hand: [sourceCard]);
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [sourceDefinition, unitDefinition]);

        var (generatedState, _) = EffectApplier.Apply(
            initial,
            initial.Allies[0],
            AddCard(unitDefinition.Id, pile: "hand"),
            Rng(),
            catalog,
            CardSource(sourceCard, sourceDefinition));
        var generated = Assert.Single(generatedState.Hand, card =>
            card.CardDefinitionId == unitDefinition.Id);
        var deadSummon = BattleFixtures.SummonActor(
                "generated-unit-actor",
                "unit-definition",
                slotIndex: 1,
                associatedCardId: generated.InstanceId)
            with { CurrentHp = 0 };
        var heldState = generatedState with
        {
            Allies = [generatedState.Allies[0], deadSummon],
            Hand = generatedState.Hand.Remove(generated),
            SummonHeld = [generated],
        };
        var events = new List<BattleEvent>();
        int order = 0;

        var cleaned = SummonCleanup.Apply(heldState, events, ref order);

        Assert.Empty(cleaned.SummonHeld);
        var returned = Assert.Single(cleaned.DiscardPile);
        Assert.Equal(CardPoolClass.Striker, returned.PoolClassOverride);
        Assert.Equal(
            CardPoolClass.Striker,
            returned.EffectivePoolClass(unitDefinition));
    }

    private static CardEffect AddCard(string cardId, string pile = "discard") =>
        new(
            "addCard",
            EffectScope.Self,
            Side: null,
            Amount: 1,
            CardRefId: cardId,
            Pile: pile);

    private static CardDefinition Card(
        string id,
        CardType type,
        CardPoolClass? poolClass,
        IReadOnlyList<CardEffect>? effects = null,
        IReadOnlyList<CardAbilityDefinition>? abilities = null) =>
        new(
            id,
            id,
            DisplayName: null,
            CardRarity.Common,
            type,
            Cost: 0,
            UpgradedCost: null,
            Effects: effects ?? [],
            UpgradedEffects: null,
            Keywords: null,
            PoolClass: poolClass,
            Abilities: abilities);

    private static BattleCardInstance Instance(
        CardDefinition definition,
        string instanceId,
        CardPoolClass? poolClassOverride = null) =>
        new(
            instanceId,
            definition.Id,
            IsUpgraded: false,
            CostOverride: null)
        {
            PoolClassOverride = poolClassOverride,
        };

    private static CombatSource CardSource(
        BattleCardInstance card,
        CardDefinition definition,
        CombatSourceKind kind = CombatSourceKind.CardEffect) =>
        new(
            kind,
            OwnerSlot: 0,
            OwnerInstanceId: "hero_inst",
            ActorInstanceId: "hero_inst",
            DefinitionId: definition.Id,
            InstanceId: card.InstanceId);

    private static PlayedCardEffectSnapshot Snapshot(
        CardPoolClass poolClass,
        string cardDefinitionId = "snapshot-source",
        string cardInstanceId = "snapshot-source-1",
        ImmutableArray<CardEffect> effects = default) =>
        new(
            cardDefinitionId,
            cardInstanceId,
            "hero_inst",
            CardType.Attack,
            OriginalCost: 0,
            PaidCost: 0,
            IsUpgraded: false,
            Keywords: [],
            Effects: effects.IsDefault ? [] : effects,
            PlaySequence: 1)
        {
            PoolClass = poolClass,
        };

    private static BattleState PutInZone(
        BattleState state,
        BattleCardInstance card,
        string zone) => zone switch
        {
            "hand" => state with { Hand = [card] },
            "draw" => state with { DrawPile = [card] },
            "discard" => state with { DiscardPile = [card] },
            "exhaust" => state with { ExhaustPile = [card] },
            "summonHeld" => state with { SummonHeld = [card] },
            "power" => state with { PowerCards = [card] },
            _ => throw new System.ArgumentOutOfRangeException(nameof(zone)),
        };

    private static IRng Rng() => new FakeRng(new int[64], []);
}
