using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public sealed class RepriseResolutionHistoryTests
{
    private static IRng Rng() => new FakeRng(new int[64], new double[16]);

    [Fact]
    public void Reprise_uses_battle_long_completion_order_including_triggered_contingency()
    {
        var contingency = Contingency(
            "delayed-source",
            new CardEffect("gainEnergy", EffectScope.Self, null, 5));
        var ordinary = Card(
            "ordinary-source",
            new CardEffect("gainEnergy", EffectScope.Self, null, 1));
        var reprise = Reprise("reprise");
        var contingencyCard = Instance(contingency, "delayed-source-1");
        var state = BattleFixtures.MinimalState(
            energy: 0,
            hand:
            [
                contingencyCard,
                Instance(ordinary, "ordinary-source-1"),
                Instance(reprise, "reprise-1"),
            ]);
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [contingency, ordinary, reprise]);

        var (armed, _) = BattleEngine.PlayCard(
            state, 0, 0, 0, Rng(), catalog);
        Assert.Empty(armed.RepriseEligibleHistory);

        var (afterOrdinary, _) = BattleEngine.PlayCard(
            armed, 0, 0, 0, Rng(), catalog);
        var ordinaryResolution = Assert.Single(afterOrdinary.RepriseEligibleHistory);
        Assert.Equal(ordinary.Id, ordinaryResolution.CardDefinitionId);
        Assert.Equal(1, ordinaryResolution.ResolutionSequence);

        var triggered = ContingencyResolutionEngine.ResolveBeforeEnemyAttack(
            afterOrdinary, Rng(), catalog);
        Assert.False(triggered.PausedForChoice);
        Assert.Equal(
            new[] { ordinary.Id, contingency.Id },
            triggered.State.RepriseEligibleHistory
                .Select(snapshot => snapshot.CardDefinitionId));
        Assert.Equal(
            new long[] { 1, 2 },
            triggered.State.RepriseEligibleHistory
                .Select(snapshot => snapshot.ResolutionSequence));

        // Reprise is no longer turn-local.  Clearing the legacy per-turn play
        // list must not remove the delayed effect from its source history.
        var nextTurn = triggered.State with
        {
            PlayedCardHistoryThisTurn =
                ImmutableArray<PlayedCardEffectSnapshot>.Empty,
        };
        var (resolved, events) = BattleEngine.PlayCard(
            nextTurn, 0, 0, 0, Rng(), catalog);

        Assert.Equal(11, resolved.Energy);
        var reenact = Assert.Single(events, battleEvent =>
            battleEvent.Kind == BattleEventKind.ReenactCardEffects);
        Assert.Equal(contingency.Id, reenact.CardId);
        Assert.Equal(
            new[] { ordinary.Id, contingency.Id, reprise.Id },
            resolved.RepriseEligibleHistory
                .Select(snapshot => snapshot.CardDefinitionId));
        Assert.Equal(3, resolved.NextEffectResolutionSequence);
    }

    [Fact]
    public void Contingency_with_choice_becomes_eligible_only_after_choice_completes()
    {
        var contingency = Contingency(
            "choice-plan",
            new CardEffect(
                "discard",
                EffectScope.Self,
                null,
                1,
                Pile: "hand",
                Select: "choose"));
        var filler = BattleFixtures.Defend();
        var contingencyCard = Instance(contingency, "choice-plan-1");
        var state = BattleFixtures.MinimalState(
            hand:
            [
                Instance(filler, "filler-1"),
                Instance(filler, "filler-2"),
            ]) with
        {
            ExhaustPile = [contingencyCard],
            ArmedContingencies =
            [
                new ArmedContingency(
                    contingencyCard.InstanceId,
                    "hero_inst",
                    new ContingencyTriggerDefinition(
                        ContingencyTriggerTiming.TurnStart),
                    ArmedOrdinal: 1),
            ],
        };
        var catalog = BattleFixtures.MinimalCatalog(cards: [contingency, filler]);

        var paused = ContingencyResolutionEngine.ResolveTurnStart(
            state, Rng(), catalog);

        Assert.True(paused.PausedForChoice);
        Assert.Empty(paused.State.RepriseEligibleHistory);

        var (resolved, _) = BattleEngine.ResolveCardChoice(
            paused.State,
            ImmutableArray.Create("filler-1"),
            Rng(),
            catalog);

        var completed = Assert.Single(resolved.RepriseEligibleHistory);
        Assert.Equal(contingency.Id, completed.CardDefinitionId);
        Assert.Equal(1, completed.ResolutionSequence);
    }

    [Fact]
    public void Ordinary_card_with_choice_becomes_eligible_only_after_its_choice()
    {
        var source = Card(
            "choice-source",
            new CardEffect(
                "discard",
                EffectScope.Self,
                null,
                1,
                Pile: "hand",
                Select: "choose"));
        var filler = BattleFixtures.Defend();
        var state = BattleFixtures.MinimalState(
            energy: 0,
            hand:
            [
                Instance(source, "choice-source-1"),
                Instance(filler, "filler-1"),
                Instance(filler, "filler-2"),
            ]);
        var catalog = BattleFixtures.MinimalCatalog(cards: [source, filler]);

        var (paused, _) = BattleEngine.PlayCard(
            state, 0, 0, 0, Rng(), catalog);
        Assert.NotNull(paused.PendingCardPlay);
        Assert.Empty(paused.RepriseEligibleHistory);

        var (resolved, _) = BattleEngine.ResolveCardChoice(
            paused,
            ImmutableArray.Create("filler-1"),
            Rng(),
            catalog);

        Assert.Equal(
            source.Id,
            Assert.Single(resolved.RepriseEligibleHistory).CardDefinitionId);
    }

    [Fact]
    public void Completing_contingency_immediately_triggers_the_owners_character_trait()
    {
        var contingency = Contingency(
            "trait-plan",
            new CardEffect("gainEnergy", EffectScope.Self, null, 2));
        var card = Instance(contingency, "trait-plan-1");
        var trait = new CharacterTraitDefinition(
            "responsive-planning",
            "Responsive Planning",
            CharacterTraitTrigger.OnContingencyTriggered,
            [
                new CardEffect("gainEnergy", EffectScope.Self, null, 1),
                new CardEffect("gainComboGauge", EffectScope.Self, null, 1),
            ]);
        var character = new CharacterDefinition(
            "contingency-character", "Contingency Character", 70, 0, 3, [],
            Trait: trait);
        var state = BattleFixtures.MinimalState(energy: 0) with
        {
            CharacterId = character.Id,
            ExhaustPile = [card],
            ArmedContingencies =
            [
                new ArmedContingency(
                    card.InstanceId,
                    "hero_inst",
                    new ContingencyTriggerDefinition(
                        ContingencyTriggerTiming.BeforeEnemyAttack),
                    ArmedOrdinal: 1),
            ],
        };
        var catalog = BattleFixtures.MinimalCatalog(cards: [contingency]) with
        {
            Characters = new Dictionary<string, CharacterDefinition>
            {
                [character.Id] = character,
            },
        };

        var result = ContingencyResolutionEngine.ResolveBeforeEnemyAttack(
            state, Rng(), catalog);

        Assert.Equal(3, result.State.Energy);
        Assert.Equal(1, result.State.ComboGauge);
        Assert.Contains(result.Events, battleEvent =>
            battleEvent.Kind == BattleEventKind.CharacterTraitTriggered
            && battleEvent.Note == trait.Id);
    }

    [Fact]
    public void Reprise_history_is_owned_and_resolved_per_participant()
    {
        var source = Card(
            "participant-source",
            new CardEffect("gainEnergy", EffectScope.Self, null, 2));
        var reprise = Reprise("participant-reprise");
        var primaryHero = BattleFixtures.Hero();
        var secondHero = BattleFixtures.Hero(slotIndex: 1) with
        {
            InstanceId = "hero-inst-2",
            DefinitionId = "hero-2",
        };
        var legacy = BattleFixtures.MinimalState(
            allies: [primaryHero, secondHero],
            energy: 0,
            hand: []);
        var state = BattleParticipantAccess.InitializePrimary(legacy);
        var primary = BattleParticipantAccess.GetPrimary(state);
        state = BattleParticipantAccess.Add(
            state,
            primary with
            {
                ParticipantId = "player:1",
                ActorInstanceId = secondHero.InstanceId,
                OwnerPlayerSlot = 1,
                Energy = 0,
                Hand =
                [
                    Instance(source, "participant-source-1"),
                    Instance(reprise, "participant-reprise-1"),
                ],
                PlayedCardHistoryThisTurn = [],
                RepriseEligibleHistory = [],
                CurrentComboCardHistory = [],
                LastFinisherRecord = [],
                ArmedContingencies = [],
            });
        var catalog = BattleFixtures.MinimalCatalog(cards: [source, reprise]);

        var (afterSource, _) = BattleEngine.PlayCard(
            state, "player:1", 0, 0, 1, Rng(), catalog);
        var (afterReprise, events) = BattleEngine.PlayCard(
            afterSource, "player:1", 0, 0, 1, Rng(), catalog);

        Assert.Empty(BattleParticipantAccess.GetPrimary(afterReprise)
            .RepriseEligibleHistory);
        Assert.True(BattleParticipantAccess.TryGet(
            afterReprise, "player:1", out var second));
        Assert.Equal(4, second.Energy);
        Assert.Equal(
            new[] { source.Id, reprise.Id },
            second.RepriseEligibleHistory
                .Select(snapshot => snapshot.CardDefinitionId));
        Assert.Equal(
            source.Id,
            Assert.Single(events, battleEvent =>
                battleEvent.Kind == BattleEventKind.ReenactCardEffects).CardId);
    }

    private static CardDefinition Card(string id, params CardEffect[] effects) =>
        new(
            id,
            id,
            null,
            CardRarity.Common,
            CardType.Skill,
            Cost: 0,
            UpgradedCost: null,
            Effects: effects,
            UpgradedEffects: null,
            Keywords: null);

    private static CardDefinition Contingency(
        string id,
        params CardEffect[] effects) =>
        new(
            id,
            id,
            null,
            CardRarity.Common,
            CardType.Skill,
            Cost: 0,
            UpgradedCost: null,
            Effects: effects,
            UpgradedEffects: null,
            Keywords: null,
            Abilities:
            [
                new ContingencyAbilityDefinition(
                    new ContingencyTriggerDefinition(
                        ContingencyTriggerTiming.BeforeEnemyAttack)),
            ]);

    private static CardDefinition Reprise(string id) =>
        new(
            id,
            id,
            null,
            CardRarity.Common,
            CardType.Skill,
            Cost: 0,
            UpgradedCost: null,
            Effects: [],
            UpgradedEffects: null,
            Keywords: null,
            Abilities:
            [
                new RepriseAbilityDefinition(
                    new RepriseCardFilter(
                        CardTypes: [CardType.Attack, CardType.Skill],
                        MaxOriginalCost: 2)),
            ]);

    private static BattleCardInstance Instance(
        CardDefinition definition,
        string instanceId) =>
        new(instanceId, definition.Id, IsUpgraded: false, CostOverride: null);
}
