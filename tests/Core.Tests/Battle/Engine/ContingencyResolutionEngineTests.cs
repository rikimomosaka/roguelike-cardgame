using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public sealed class ContingencyResolutionEngineTests
{
    private static IRng Rng() =>
        new FakeRng(new int[64], new double[16]);

    [Fact]
    public void TurnStart_resolves_in_arming_order_and_moves_cards_to_discard()
    {
        var first = Contingency(
            "first",
            ContingencyTriggerTiming.TurnStart,
            new CardEffect("gainEnergy", EffectScope.Self, null, 1));
        var second = Contingency(
            "second",
            ContingencyTriggerTiming.TurnStart,
            new CardEffect("gainEnergy", EffectScope.Self, null, 2));
        var firstCard = Card(first, "first-1");
        var secondCard = Card(second, "second-1");
        var state = ArmedState(
            [secondCard, firstCard],
            [
                Armed(secondCard, ContingencyTriggerTiming.TurnStart, 2),
                Armed(firstCard, ContingencyTriggerTiming.TurnStart, 1),
            ]) with { Energy = 0 };
        var catalog = BattleFixtures.MinimalCatalog(cards: [first, second]);

        var result = ContingencyResolutionEngine.ResolveTurnStart(
            state, Rng(), catalog);

        Assert.False(result.PausedForChoice);
        Assert.Equal(3, result.State.Energy);
        Assert.Empty(result.State.ExhaustPile);
        Assert.Empty(result.State.ArmedContingencies);
        Assert.Equal(
            new[] { "first", "second" },
            result.State.DiscardPile.Select(card => card.CardDefinitionId));
        Assert.Equal(
            new[] { "first", "second" },
            result.Events
                .Where(battleEvent => battleEvent.Kind
                    == BattleEventKind.ContingencyTriggered)
                .Select(battleEvent => battleEvent.CardId));
        Assert.Equal(
            new[] { "first-1", "second-1" },
            result.Events
                .Where(battleEvent => battleEvent.Kind
                    == BattleEventKind.ContingencyTriggered)
                .Select(battleEvent => battleEvent.ReenactedCard?.CardInstanceId));
    }

    [Fact]
    public void Exhaust_keyword_keeps_triggered_card_in_exhaust_but_disarms_it()
    {
        var definition = Contingency(
            "lost-plan",
            ContingencyTriggerTiming.TurnStart,
            new CardEffect("gainEnergy", EffectScope.Self, null, 1),
            keywords: ["exhaust"]);
        var card = Card(definition, "lost-plan-1");
        var state = ArmedState(
            [card],
            [Armed(card, ContingencyTriggerTiming.TurnStart, 1)]);

        var result = ContingencyResolutionEngine.ResolveTurnStart(
            state,
            Rng(),
            BattleFixtures.MinimalCatalog(cards: [definition]));

        Assert.Equal(card.InstanceId, Assert.Single(result.State.ExhaustPile).InstanceId);
        Assert.Empty(result.State.DiscardPile);
        Assert.Empty(result.State.ArmedContingencies);
        var completion = Assert.Single(result.Events, battleEvent =>
            battleEvent.Kind == BattleEventKind.ContingencyResolved);
        Assert.Equal("exhaust", completion.Note);
        Assert.Equal(card.InstanceId, completion.ReenactedCard?.CardInstanceId);
    }

    [Fact]
    public void Completion_event_reports_the_actual_zone_when_an_effect_moves_its_own_card()
    {
        var definition = Contingency(
            "self-recovery-plan",
            ContingencyTriggerTiming.TurnStart,
            new CardEffect(
                "recoverFromExhaust",
                EffectScope.Self,
                null,
                1,
                Pile: "hand",
                Select: "all"));
        var card = Card(definition, "self-recovery-plan-1");
        var state = ArmedState(
            [card],
            [Armed(card, ContingencyTriggerTiming.TurnStart, 1)]);

        var result = ContingencyResolutionEngine.ResolveTurnStart(
            state,
            Rng(),
            BattleFixtures.MinimalCatalog(cards: [definition]));

        Assert.Equal(card.InstanceId, Assert.Single(result.State.Hand).InstanceId);
        Assert.Empty(result.State.ExhaustPile);
        Assert.Empty(result.State.DiscardPile);
        var completion = Assert.Single(result.Events, battleEvent =>
            battleEvent.Kind == BattleEventKind.ContingencyResolved);
        Assert.Equal("hand", completion.Note);
    }

    [Fact]
    public void Trigger_uses_the_cards_current_upgraded_effect_list()
    {
        var definition = new CardDefinition(
            "upgrade-plan",
            "upgrade-plan",
            null,
            CardRarity.Common,
            CardType.Skill,
            Cost: 0,
            UpgradedCost: 0,
            Effects: [new CardEffect("gainEnergy", EffectScope.Self, null, 1)],
            UpgradedEffects: [new CardEffect("gainEnergy", EffectScope.Self, null, 5)],
            Keywords: null,
            Abilities:
            [
                new ContingencyAbilityDefinition(
                    new ContingencyTriggerDefinition(
                        ContingencyTriggerTiming.TurnStart)),
            ],
            UpgradedAbilities:
            [
                new ContingencyAbilityDefinition(
                    new ContingencyTriggerDefinition(
                        ContingencyTriggerTiming.TurnStart)),
            ]);
        var card = new BattleCardInstance(
            "upgrade-plan-1", definition.Id, IsUpgraded: true, CostOverride: null);
        var state = ArmedState(
            [card],
            [Armed(card, ContingencyTriggerTiming.TurnStart, 1)]) with
        {
            Energy = 0,
        };

        var result = ContingencyResolutionEngine.ResolveTurnStart(
            state,
            Rng(),
            BattleFixtures.MinimalCatalog(cards: [definition]));

        Assert.Equal(5, result.State.Energy);
    }

    [Fact]
    public void BeforeEnemyAttack_resolves_new_attack_hits_immediately()
    {
        var definition = Contingency(
            "counter",
            ContingencyTriggerTiming.BeforeEnemyAttack,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 4));
        var card = Card(definition, "counter-1");
        var state = ArmedState(
            [card],
            [Armed(card, ContingencyTriggerTiming.BeforeEnemyAttack, 1)]) with
        {
            Phase = BattlePhase.EnemyAttacking,
        };

        var result = ContingencyResolutionEngine.ResolveBeforeEnemyAttack(
            state,
            Rng(),
            BattleFixtures.MinimalCatalog(cards: [definition]));

        Assert.Equal(16, result.State.Enemies[0].CurrentHp);
        Assert.Contains(result.Events, battleEvent =>
            battleEvent.Kind == BattleEventKind.DealDamage
            && battleEvent.Amount == 4);
        Assert.Equal(card.InstanceId, Assert.Single(result.State.DiscardPile).InstanceId);
    }

    [Fact]
    public void ComboReached_requires_an_edge_crossing_for_the_owning_actor()
    {
        var definition = Contingency(
            "combo-plan",
            ContingencyTriggerTiming.ComboReached,
            new CardEffect("gainEnergy", EffectScope.Self, null, 2),
            comboThreshold: 3);
        var card = Card(definition, "combo-plan-1");
        var state = ArmedState(
            [card],
            [Armed(
                card,
                ContingencyTriggerTiming.ComboReached,
                1,
                comboThreshold: 3)]) with { Energy = 0, ComboCount = 3 };
        var catalog = BattleFixtures.MinimalCatalog(cards: [definition]);

        var noEdge = ContingencyResolutionEngine.ResolveComboReached(
            state, "hero_inst", 3, 4, Rng(), catalog);
        Assert.Equal(0, noEdge.State.Energy);
        Assert.Single(noEdge.State.ArmedContingencies);

        var crossed = ContingencyResolutionEngine.ResolveComboReached(
            state, "hero_inst", 2, 3, Rng(), catalog);
        Assert.Equal(2, crossed.State.Energy);
        Assert.Empty(crossed.State.ArmedContingencies);
    }

    [Fact]
    public void Missing_exhaust_membership_disarms_without_firing()
    {
        var definition = Contingency(
            "removed-plan",
            ContingencyTriggerTiming.TurnStart,
            new CardEffect("gainEnergy", EffectScope.Self, null, 4));
        var card = Card(definition, "removed-plan-1");
        var state = ArmedState(
            [],
            [Armed(card, ContingencyTriggerTiming.TurnStart, 1)]) with
        {
            Energy = 0,
        };

        var result = ContingencyResolutionEngine.ResolveTurnStart(
            state,
            Rng(),
            BattleFixtures.MinimalCatalog(cards: [definition]));

        Assert.Equal(0, result.State.Energy);
        Assert.Empty(result.State.ArmedContingencies);
        Assert.DoesNotContain(result.Events, battleEvent =>
            battleEvent.Kind == BattleEventKind.ContingencyTriggered);
    }

    [Fact]
    public void Human_choose_effect_pauses_after_disarming_and_before_destination()
    {
        var definition = Contingency(
            "choice-plan",
            ContingencyTriggerTiming.TurnStart,
            new CardEffect(
                "discard",
                EffectScope.Self,
                null,
                1,
                Pile: "hand",
                Select: "choose"));
        var card = Card(definition, "choice-plan-1");
        var filler = BattleFixtures.Defend();
        var state = ArmedState(
            [card],
            [Armed(card, ContingencyTriggerTiming.TurnStart, 1)]) with
        {
            Hand =
            [
                Card(filler, "filler-1"),
                Card(filler, "filler-2"),
            ],
        };

        var result = ContingencyResolutionEngine.ResolveTurnStart(
            state,
            Rng(),
            BattleFixtures.MinimalCatalog(cards: [definition, filler]));

        Assert.True(result.PausedForChoice);
        Assert.Empty(result.State.ArmedContingencies);
        Assert.Equal(card.InstanceId, Assert.Single(result.State.ExhaustPile).InstanceId);
        Assert.Empty(result.State.DiscardPile);
        Assert.Equal(
            EffectExecutionKind.Contingency,
            result.State.PendingCardPlay?.EffectExecution?.Kind);
        Assert.Equal(
            new[] { "filler-1", "filler-2" },
            result.State.PendingCardPlay?.Choice.CandidateInstanceIds);
        Assert.DoesNotContain(result.Events, battleEvent =>
            battleEvent.Kind == BattleEventKind.ContingencyResolved);

        var (completed, completionEvents) = BattleEngine.ResolveCardChoice(
            result.State,
            ImmutableArray.Create("filler-1"),
            Rng(),
            BattleFixtures.MinimalCatalog(cards: [definition, filler]));

        Assert.Null(completed.PendingCardPlay);
        Assert.Empty(completed.ExhaustPile);
        Assert.Equal(card.InstanceId, completed.DiscardPile[^1].InstanceId);
        var completion = Assert.Single(completionEvents, battleEvent =>
            battleEvent.Kind == BattleEventKind.ContingencyResolved);
        Assert.Equal("discard", completion.Note);
        Assert.Equal(card.InstanceId, completion.ReenactedCard?.CardInstanceId);
    }

    [Fact]
    public void Cpu_owner_resolves_choose_with_the_existing_first_candidate_policy()
    {
        var definition = Contingency(
            "cpu-plan",
            ContingencyTriggerTiming.TurnStart,
            new CardEffect(
                "discard",
                EffectScope.Self,
                null,
                1,
                Pile: "hand",
                Select: "choose"));
        var card = Card(definition, "cpu-plan-1");
        var filler = BattleFixtures.Defend();
        var legacy = ArmedState(
            [card],
            [Armed(card, ContingencyTriggerTiming.TurnStart, 1)]) with
        {
            Hand =
            [
                Card(filler, "filler-1"),
                Card(filler, "filler-2"),
            ],
        };
        var state = BattleParticipantAccess.InitializePrimary(
            legacy,
            controllerKind: BattleParticipantControllerKind.Cpu);

        var result = ContingencyResolutionEngine.ResolveTurnStart(
            state,
            Rng(),
            BattleFixtures.MinimalCatalog(cards: [definition, filler]));

        Assert.False(result.PausedForChoice);
        Assert.Null(result.State.PendingCardPlay);
        Assert.Equal(
            "filler-2",
            Assert.Single(result.State.Hand).InstanceId);
        Assert.Equal(
            new[] { "filler-1", "cpu-plan-1" },
            result.State.DiscardPile.Select(item => item.InstanceId));
        Assert.Empty(BattleParticipantAccess.GetPrimary(result.State)
            .ArmedContingencies);
    }

    [Fact]
    public void TurnStart_hook_runs_after_base_energy_refresh()
    {
        var definition = Contingency(
            "morning-plan",
            ContingencyTriggerTiming.TurnStart,
            new CardEffect("gainEnergy", EffectScope.Self, null, 2));
        var card = Card(definition, "morning-plan-1");
        var state = ArmedState(
            [card],
            [Armed(card, ContingencyTriggerTiming.TurnStart, 1)]) with
        {
            Energy = 0,
            EnergyMax = 3,
            DrawPerTurn = 0,
        };

        var (next, events) = TurnStartProcessor.Process(
            state,
            Rng(),
            BattleFixtures.MinimalCatalog(cards: [definition]));

        Assert.Equal(5, next.Energy);
        Assert.Equal(card.InstanceId, Assert.Single(next.DiscardPile).InstanceId);
        Assert.Contains(events, battleEvent =>
            battleEvent.Kind == BattleEventKind.ContingencyTriggered);
    }

    [Fact]
    public void Enemy_action_hook_fires_once_before_the_first_hit_of_a_multi_hit_move()
    {
        var definition = Contingency(
            "guard-plan",
            ContingencyTriggerTiming.BeforeEnemyAttack,
            new CardEffect("block", EffectScope.Self, null, 4));
        var card = Card(definition, "guard-plan-1");
        var enemyDefinition = new EnemyDefinition(
            "multi",
            "multi",
            "img_multi",
            20,
            new EnemyPool(1, EnemyTier.Weak),
            "double",
            [
                new MoveDefinition(
                    "double",
                    MoveKind.Attack,
                    [
                        new CardEffect(
                            "attack", EffectScope.All, EffectSide.Enemy, 3),
                        new CardEffect(
                            "attack", EffectScope.All, EffectSide.Enemy, 3),
                    ],
                    "double"),
            ]);
        var enemy = BattleFixtures.Goblin() with
        {
            DefinitionId = enemyDefinition.Id,
            CurrentMoveId = "double",
        };
        var state = ArmedState(
            [card],
            [Armed(card, ContingencyTriggerTiming.BeforeEnemyAttack, 1)]) with
        {
            Phase = BattlePhase.EnemyAttacking,
            Enemies = [enemy],
        };

        var (next, events) = EnemyAttackingResolver.Resolve(
            state,
            Rng(),
            BattleFixtures.MinimalCatalog(
                cards: [definition],
                enemies: [enemyDefinition]));

        Assert.Equal(68, next.Allies[0].CurrentHp);
        Assert.Single(events, battleEvent =>
            battleEvent.Kind == BattleEventKind.ContingencyTriggered);
        Assert.Equal(
            2,
            events.Count(battleEvent =>
                battleEvent.Kind == BattleEventKind.DealDamage));
    }

    [Fact]
    public void ComboReached_card_can_arm_and_trigger_itself_on_the_same_play()
    {
        var definition = Contingency(
            "instant-plan",
            ContingencyTriggerTiming.ComboReached,
            new CardEffect("gainEnergy", EffectScope.Self, null, 3),
            comboThreshold: 1);
        var card = Card(definition, "instant-plan-1");
        var state = BattleFixtures.MinimalState(
            energy: 0,
            hand: [card]);

        var (next, events) = BattleEngine.PlayCard(
            state,
            handIndex: 0,
            targetEnemyIndex: 0,
            targetAllyIndex: 0,
            Rng(),
            BattleFixtures.MinimalCatalog(cards: [definition]));

        Assert.Equal(3, next.Energy);
        Assert.Empty(next.Hand);
        Assert.Empty(next.ExhaustPile);
        Assert.Empty(next.ArmedContingencies);
        Assert.Equal(card.InstanceId, Assert.Single(next.DiscardPile).InstanceId);
        Assert.Equal(
            new[]
            {
                BattleEventKind.ContingencyArmed,
                BattleEventKind.ContingencyTriggered,
            },
            events
                .Where(battleEvent => battleEvent.Kind is
                    BattleEventKind.ContingencyArmed
                    or BattleEventKind.ContingencyTriggered)
                .Select(battleEvent => battleEvent.Kind));
    }

    [Fact]
    public void BeforeEnemyAttack_counter_does_not_hit_expired_enemy_block()
    {
        var definition = Contingency(
            "counter-expired-block",
            ContingencyTriggerTiming.BeforeEnemyAttack,
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 4));
        var card = Card(definition, "counter-expired-block-1");
        var enemyDefinition = new EnemyDefinition(
            "blocked-attacker",
            "blocked-attacker",
            "img_blocked_attacker",
            20,
            new EnemyPool(1, EnemyTier.Weak),
            "attack",
            [
                new MoveDefinition(
                    "attack",
                    MoveKind.Attack,
                    [new CardEffect(
                        "attack", EffectScope.All, EffectSide.Enemy, 1)],
                    "attack"),
            ]);
        var enemy = BattleFixtures.Goblin() with
        {
            DefinitionId = enemyDefinition.Id,
            CurrentMoveId = "attack",
            CurrentHp = 20,
            MaxHp = 20,
            Block = new BlockPool(10, 0),
        };
        var state = ArmedState(
            [card],
            [Armed(card, ContingencyTriggerTiming.BeforeEnemyAttack, 1)]) with
        {
            Phase = BattlePhase.EnemyAttacking,
            Enemies = [enemy],
        };

        var (next, _) = EnemyAttackingResolver.Resolve(
            state,
            Rng(),
            BattleFixtures.MinimalCatalog(
                cards: [definition],
                enemies: [enemyDefinition]));

        Assert.Equal(16, next.Enemies[0].CurrentHp);
        Assert.Equal(0, next.Enemies[0].Block.Sum);
    }

    private static BattleState ArmedState(
        ImmutableArray<BattleCardInstance> exhaust,
        ImmutableArray<ArmedContingency> armed) =>
        BattleFixtures.MinimalState() with
        {
            ExhaustPile = exhaust,
            ArmedContingencies = armed,
        };

    private static ArmedContingency Armed(
        BattleCardInstance card,
        ContingencyTriggerTiming timing,
        long ordinal,
        int? comboThreshold = null) =>
        new(
            card.InstanceId,
            "hero_inst",
            new ContingencyTriggerDefinition(timing, comboThreshold),
            ordinal);

    private static CardDefinition Contingency(
        string id,
        ContingencyTriggerTiming timing,
        CardEffect effect,
        int? comboThreshold = null,
        string[]? keywords = null) =>
        new(
            id,
            id,
            null,
            CardRarity.Common,
            CardType.Skill,
            Cost: 0,
            UpgradedCost: null,
            Effects: [effect],
            UpgradedEffects: null,
            Keywords: keywords,
            Abilities:
            [
                new ContingencyAbilityDefinition(
                    new ContingencyTriggerDefinition(timing, comboThreshold)),
            ]);

    private static BattleCardInstance Card(
        CardDefinition definition,
        string instanceId) =>
        new(instanceId, definition.Id, IsUpgraded: false, CostOverride: null);
}
