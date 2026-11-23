using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public sealed class BattleEngineCardAbilityRuntimeTests
{
    private static IRng Rng() => new FakeRng(new int[64], new double[16]);

    [Fact]
    public void Momentum_uses_live_turn_maximum_when_effect_is_executed()
    {
        var definition = Card(
            "momentum",
            CardType.Attack,
            0,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5),
            new CardEffect(
                "attack", EffectScope.Single, EffectSide.Enemy, 11,
                MaxComboMin: 3));
        var card = Instance(definition, "momentum-1");
        var state = BattleFixtures.MinimalState(
            hand: ImmutableArray.Create(card)) with
        {
            ComboCount = 1,
            MaxComboThisTurn = 3,
            LastPlayedOrigCost = 5,
        };
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { definition });

        var (next, _) = BattleEngine.PlayCard(
            state, 0, 0, 0, Rng(), catalog);

        Assert.Equal(16, next.Allies[0].AttackSingle.Sum);
    }

    [Fact]
    public void Finisher_saves_all_attack_cards_in_combo_and_resets_only_current_combo()
    {
        var opening = Card(
            "opening", CardType.Attack, 0,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 3));
        var finisher = new CardDefinition(
            "finisher", "finisher", null,
            CardRarity.Common, CardType.Attack,
            Cost: 1, UpgradedCost: null,
            Effects: new[]
            {
                new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 7),
            },
            UpgradedEffects: null,
            Keywords: null,
            Abilities: new CardAbilityDefinition[]
            {
                new FinisherAbilityDefinition(),
            });
        var state = BattleFixtures.MinimalState(
            hand: ImmutableArray.Create(
                Instance(opening, "opening-1"),
                Instance(finisher, "finisher-1"))) with { Energy = 3 };
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { opening, finisher });

        var (afterOpening, _) = BattleEngine.PlayCard(
            state, 0, 0, 0, Rng(), catalog);
        var (afterFinisher, events) = BattleEngine.PlayCard(
            afterOpening, 0, 0, 0, Rng(), catalog);

        Assert.Equal(0, afterFinisher.ComboCount);
        Assert.Equal(2, afterFinisher.MaxComboThisTurn);
        Assert.Equal(2, Assert.Single(
            events, battleEvent => battleEvent.Kind == BattleEventKind.PlayCard)
            .ComboCountAfter);
        Assert.Null(afterFinisher.LastPlayedOrigCost);
        Assert.Empty(afterFinisher.CurrentComboCardHistory);
        Assert.Equal(
            new[] { "opening", "finisher" },
            afterFinisher.LastFinisherRecord
                .Select(snapshot => snapshot.CardDefinitionId));
        var resolved = Assert.Single(
            events, battleEvent => battleEvent.Kind == BattleEventKind.FinisherResolved);
        Assert.Equal(2, resolved.Amount);
    }

    [Fact]
    public void Contingency_skips_effects_and_arms_in_normal_exhaust_pile()
    {
        var contingency = new CardDefinition(
            "contingency", "contingency", null,
            CardRarity.Common, CardType.Skill,
            Cost: 0, UpgradedCost: null,
            Effects: new[]
            {
                new CardEffect("gainEnergy", EffectScope.Self, null, 5),
            },
            UpgradedEffects: null,
            Keywords: new[] { "exhaust", "wait" },
            Abilities: new CardAbilityDefinition[]
            {
                new ContingencyAbilityDefinition(
                    new ContingencyTriggerDefinition(
                        ContingencyTriggerTiming.BeforeEnemyAttack)),
            });
        var card = Instance(contingency, "contingency-1");
        var state = BattleFixtures.MinimalState(
            hand: ImmutableArray.Create(card)) with { Energy = 1 };
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { contingency });

        var (next, events) = BattleEngine.PlayCard(
            state, 0, 0, 0, Rng(), catalog);

        Assert.Equal(1, next.Energy);
        Assert.Empty(next.Hand);
        Assert.Equal("contingency-1", Assert.Single(next.ExhaustPile).InstanceId);
        Assert.Equal("contingency-1", Assert.Single(next.ArmedContingencies).CardInstanceId);
        Assert.Contains(events, battleEvent =>
            battleEvent.Kind == BattleEventKind.ContingencyArmed);
    }

    [Fact]
    public void Reprise_rechecks_momentum_against_live_max_combo()
    {
        var source = Card(
            "source", CardType.Skill, 0,
            new CardEffect("gainEnergy", EffectScope.Self, null, 1),
            new CardEffect(
                "gainEnergy", EffectScope.Self, null, 5,
                MaxComboMin: 2));
        var reprise = RepriseCard("reprise", 1, CardType.Skill, maxOriginalCost: 3);
        var state = BattleFixtures.MinimalState(
            hand: ImmutableArray.Create(
                Instance(source, "source-1"),
                Instance(reprise, "reprise-1"))) with { Energy = 0 };
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { source, reprise });

        var (afterSource, _) = BattleEngine.PlayCard(
            state, 0, 0, 0, Rng(), catalog);
        var (afterReprise, events) = BattleEngine.PlayCard(
            afterSource, 0, 0, 0, Rng(), catalog);

        Assert.Equal(7, afterReprise.Energy);
        Assert.Empty(afterReprise.CardUsesThisTurn);
        Assert.Equal(2, afterReprise.PlayedCardHistoryThisTurn.Length);
        var reenact = Assert.Single(events, battleEvent =>
            battleEvent.Kind == BattleEventKind.ReenactCardEffects);
        Assert.Equal(source.Id, reenact.CardId);
        var sourceMetadata = Assert.IsType<CombatSource>(reenact.Source);
        Assert.Equal(source.Id, sourceMetadata.DefinitionId);
        Assert.Equal("source-1", sourceMetadata.InstanceId);
        Assert.Equal("reenact", sourceMetadata.Scope);
        Assert.Equal(
            afterReprise.RepriseEligibleHistory[0],
            reenact.ReenactedCard);
    }

    [Fact]
    public void Reprise_choice_is_presented_again_and_resumes_triggering_card()
    {
        var source = Card(
            "source-choice", CardType.Skill, 0,
            new CardEffect(
                "discard", EffectScope.Self, null, 1,
                Pile: "hand", Select: "choose", MaxComboMin: 2));
        var reprise = RepriseCard(
            "reprise-choice", 1, CardType.Skill, maxOriginalCost: 3);
        var filler = BattleFixtures.Defend();
        var state = BattleFixtures.MinimalState(
            hand: ImmutableArray.Create(
                Instance(source, "source-choice-1"),
                Instance(reprise, "reprise-choice-1"),
                Instance(filler, "filler-1"),
                Instance(filler, "filler-2"))) with { Energy = 1 };
        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { source, reprise, filler });

        var (afterSource, _) = BattleEngine.PlayCard(
            state, 0, 0, 0, Rng(), catalog);
        var (paused, _) = BattleEngine.PlayCard(
            afterSource, 0, 0, 0, Rng(), catalog);

        Assert.NotNull(paused.PendingCardPlay?.EffectExecution);
        Assert.Equal(
            EffectExecutionKind.Reprise,
            paused.PendingCardPlay!.EffectExecution!.Kind);
        Assert.Equal(
            new[] { "filler-1", "filler-2" },
            paused.PendingCardPlay.Choice.CandidateInstanceIds.AsEnumerable());

        var (resolved, _) = BattleEngine.ResolveCardChoice(
            paused,
            ImmutableArray.Create("filler-1"),
            Rng(),
            catalog);

        Assert.Null(resolved.PendingCardPlay);
        Assert.Equal("filler-2", Assert.Single(resolved.Hand).InstanceId);
        Assert.Contains(resolved.DiscardPile, card => card.InstanceId == "filler-1");
        Assert.Contains(resolved.DiscardPile, card => card.InstanceId == "reprise-choice-1");
    }

    [Fact]
    public void Afterimage_reexecutes_every_saved_effect_in_order_for_each_stack()
    {
        var source = Card(
            "afterimage-source", CardType.Attack, 1,
            new CardEffect("debuff", EffectScope.Single, EffectSide.Enemy, 1,
                Name: BattleStatusIds.Weak),
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5),
            new CardEffect("gainEnergy", EffectScope.Self, null, 1));
        var snapshot = new PlayedCardEffectSnapshot(
            source.Id,
            "afterimage-source-1",
            "hero_inst",
            CardType.Attack,
            1,
            1,
            false,
            ImmutableArray<string>.Empty,
            source.Effects.ToImmutableArray(),
            1);
        var hero = BattleFixtures.Hero() with
        {
            Statuses = BattleFixtures.Hero().Statuses.SetItem(
                BattleStatusIds.Afterimage, 2),
        };
        var state = BattleFixtures.MinimalState() with
        {
            Phase = BattlePhase.PlayerAttacking,
            Allies = ImmutableArray.Create(hero),
            Energy = 0,
            LastFinisherRecord = ImmutableArray.Create(snapshot),
        };
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { source });

        var (next, events, paused) = BattleEngine.ResolveAfterimageAtActorEnd(
            state, hero.InstanceId, Rng(), catalog, 0);

        Assert.False(paused);
        Assert.Equal(10, next.Enemies[0].CurrentHp);
        Assert.Equal(2, next.Enemies[0].GetStatus(BattleStatusIds.Weak));
        Assert.Equal(2, next.Energy);
        Assert.Equal(0, next.Allies[0].GetStatus(BattleStatusIds.Afterimage));
        var reenactEvents = events
            .Where(battleEvent =>
                battleEvent.Kind == BattleEventKind.ReenactCardEffects)
            .ToArray();
        Assert.Equal(2, reenactEvents.Length);
        Assert.All(reenactEvents, battleEvent =>
        {
            var sourceMetadata = Assert.IsType<CombatSource>(battleEvent.Source);
            Assert.Equal(source.Id, sourceMetadata.DefinitionId);
            Assert.Equal(snapshot.CardInstanceId, sourceMetadata.InstanceId);
            Assert.Equal("reenact", sourceMetadata.Scope);
            Assert.Equal(snapshot, battleEvent.ReenactedCard);
        });
    }

    [Fact]
    public void Afterimage_choice_pauses_and_resumes_the_copied_effect_list()
    {
        var source = Card(
            "afterimage-choice-source", CardType.Attack, 1,
            new CardEffect(
                "discard", EffectScope.Self, null, 1,
                Pile: "hand", Select: "choose"),
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5));
        var snapshot = new PlayedCardEffectSnapshot(
            source.Id,
            "afterimage-source-used",
            "hero_inst",
            CardType.Attack,
            1,
            1,
            false,
            ImmutableArray<string>.Empty,
            source.Effects.ToImmutableArray(),
            1);
        var hero = BattleFixtures.Hero() with
        {
            Statuses = BattleFixtures.Hero().Statuses.SetItem(
                BattleStatusIds.Afterimage, 1),
        };
        var filler = BattleFixtures.Defend();
        var state = BattleFixtures.MinimalState(
            hand: ImmutableArray.Create(
                Instance(filler, "choice-1"),
                Instance(filler, "choice-2"))) with
        {
            Phase = BattlePhase.PlayerAttacking,
            Allies = ImmutableArray.Create(hero),
            LastFinisherRecord = ImmutableArray.Create(snapshot),
        };
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { source, filler });

        var (paused, _, didPause) = BattleEngine.ResolveAfterimageAtActorEnd(
            state, hero.InstanceId, Rng(), catalog, 0);

        Assert.True(didPause);
        Assert.Equal(
            EffectExecutionKind.Afterimage,
            paused.PendingCardPlay?.EffectExecution?.Kind);

        var (resolved, _) = BattleEngine.ResolveCardChoice(
            paused,
            ImmutableArray.Create("choice-1"),
            Rng(),
            catalog);

        Assert.Null(resolved.PendingCardPlay);
        Assert.Equal(15, resolved.Enemies[0].CurrentHp);
        Assert.Equal("choice-2", Assert.Single(resolved.Hand).InstanceId);
        Assert.Contains(resolved.DiscardPile, card => card.InstanceId == "choice-1");
    }

    [Fact]
    public void Combo_contingency_choice_finishes_before_the_triggering_finisher_resets_combo()
    {
        var contingency = new CardDefinition(
            "combo-contingency", "combo-contingency", null,
            CardRarity.Common, CardType.Skill,
            Cost: 0, UpgradedCost: null,
            Effects: new CardEffect[]
            {
                new("gainEnergy", EffectScope.Self, null, 5, MaxComboMin: 2),
                new("discard", EffectScope.Self, null, 1,
                    Pile: "hand", Select: "choose", MaxComboMin: 2),
            },
            UpgradedEffects: null,
            Keywords: null,
            Abilities: new CardAbilityDefinition[]
            {
                new ContingencyAbilityDefinition(
                    new ContingencyTriggerDefinition(
                        ContingencyTriggerTiming.ComboReached,
                        ComboThreshold: 2)),
            });
        var finisher = new CardDefinition(
            "choice-finisher", "choice-finisher", null,
            CardRarity.Common, CardType.Attack,
            Cost: 1, UpgradedCost: null,
            Effects: new[]
            {
                new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 7),
            },
            UpgradedEffects: null,
            Keywords: null,
            Abilities: new CardAbilityDefinition[]
            {
                new FinisherAbilityDefinition(),
            });
        var filler = BattleFixtures.Defend();
        var state = BattleFixtures.MinimalState(
            hand: ImmutableArray.Create(
                Instance(contingency, "contingency-choice-1"),
                Instance(finisher, "finisher-choice-1"),
                Instance(filler, "filler-choice-1"),
                Instance(filler, "filler-choice-2"))) with { Energy = 1 };
        var catalog = BattleFixtures.MinimalCatalog(
            cards: new[] { contingency, finisher, filler });

        var (armed, _) = BattleEngine.PlayCard(
            state, 0, 0, 0, Rng(), catalog);
        var (paused, _) = BattleEngine.PlayCard(
            armed, 0, 0, 0, Rng(), catalog);

        Assert.Equal(2, paused.ComboCount);
        Assert.Equal(6, paused.Energy);
        Assert.Equal(
            EffectExecutionKind.Contingency,
            paused.PendingCardPlay?.EffectExecution?.Kind);
        Assert.Equal(
            "finisher-choice-1",
            paused.PendingCardPlay?.EffectExecution?
                .PostContingencyFinisherCard?.InstanceId);

        var (resolved, events) = BattleEngine.ResolveCardChoice(
            paused,
            ImmutableArray.Create("filler-choice-1"),
            Rng(),
            catalog);

        Assert.Null(resolved.PendingCardPlay);
        Assert.Equal(0, resolved.ComboCount);
        Assert.Equal(2, resolved.MaxComboThisTurn);
        Assert.Equal(
            "choice-finisher",
            Assert.Single(resolved.LastFinisherRecord).CardDefinitionId);
        Assert.Contains(events, battleEvent =>
            battleEvent.Kind == BattleEventKind.FinisherResolved
            && battleEvent.Amount == 2);
        Assert.Contains(resolved.DiscardPile, card =>
            card.InstanceId == "contingency-choice-1");
        Assert.Contains(resolved.DiscardPile, card =>
            card.InstanceId == "filler-choice-1");
    }

    private static CardDefinition Card(
        string id,
        CardType type,
        int cost,
        params CardEffect[] effects) =>
        new(
            id, id, null,
            CardRarity.Common, type,
            Cost: cost, UpgradedCost: null,
            Effects: effects,
            UpgradedEffects: null,
            Keywords: null);

    private static CardDefinition RepriseCard(
        string id,
        int cost,
        CardType sourceType,
        int maxOriginalCost) =>
        new(
            id, id, null,
            CardRarity.Common, CardType.Skill,
            Cost: cost, UpgradedCost: null,
            Effects: System.Array.Empty<CardEffect>(),
            UpgradedEffects: null,
            Keywords: null,
            Abilities: new CardAbilityDefinition[]
            {
                new RepriseAbilityDefinition(
                    new RepriseCardFilter(
                        CardTypes: new[] { sourceType },
                        MaxOriginalCost: maxOriginalCost)),
            });

    private static BattleCardInstance Instance(
        CardDefinition definition,
        string instanceId) =>
        new(instanceId, definition.Id, false, null);
}
