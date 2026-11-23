using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public sealed class QueuedTurnChoiceContinuationTests
{
    private static IRng Rng() => new FakeRng(new int[64], new double[16]);

    [Fact]
    public void Afterimage_choice_resumes_with_only_the_remaining_allies()
    {
        var copied = Card(
            "afterimage-choice",
            new CardEffect(
                "discard", EffectScope.Self, null, 1,
                Pile: "hand", Select: "choose"),
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5));
        var filler = BattleFixtures.Defend();
        var hero = BattleFixtures.Hero() with
        {
            AttackSingle = AttackPool.Empty.Add(3),
            Statuses = BattleFixtures.Hero().Statuses.SetItem(
                BattleStatusIds.Afterimage, 1),
        };
        var snapshot = Snapshot(copied, hero.InstanceId, "copied-used", 1);
        var state = BattleFixtures.MinimalState(
            allies: [hero],
            enemies: [BattleFixtures.Goblin(0, 50)],
            hand:
            [
                Instance(filler, "choice-a"),
                Instance(filler, "choice-b"),
            ]) with
        {
            LastFinisherRecord = [snapshot],
            DrawPerTurn = 0,
        };
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [copied, filler]);

        var (paused, firstEvents) = BattleEngine.EndTurn(
            state, Rng(), catalog);

        Assert.Equal(BattlePhase.PlayerAttacking, paused.Phase);
        Assert.Equal(47, paused.Enemies[0].CurrentHp);
        Assert.Equal(
            TurnResolutionContinuationStage.PlayerAttacking,
            paused.PendingTurnResolution?.Stage);
        Assert.Equal(
            EffectExecutionKind.Afterimage,
            paused.PendingCardPlay?.EffectExecution?.Kind);
        Assert.Single(firstEvents, battleEvent =>
            battleEvent.Kind == BattleEventKind.DealDamage
            && battleEvent.TargetInstanceId == paused.Enemies[0].InstanceId);

        var (resumed, resumedEvents) = BattleEngine.ResolveCardChoice(
            paused,
            ImmutableArray.Create("choice-a"),
            Rng(),
            catalog);

        Assert.Null(resumed.PendingCardPlay);
        Assert.Null(resumed.PendingTurnResolution);
        Assert.Equal(BattlePhase.PlayerInput, resumed.Phase);
        Assert.Equal(2, resumed.Turn);
        Assert.Equal(42, resumed.Enemies[0].CurrentHp);
        Assert.Single(resumedEvents, battleEvent =>
            battleEvent.Kind == BattleEventKind.DealDamage
            && battleEvent.TargetInstanceId == resumed.Enemies[0].InstanceId);
        Assert.Contains(resumedEvents, battleEvent =>
            battleEvent.Kind == BattleEventKind.TurnStart);
    }

    [Fact]
    public void Afterimage_choice_does_not_run_later_saved_cards_after_a_lethal_attack()
    {
        var lethalAfterChoice = Card(
            "afterimage-lethal-choice",
            new CardEffect(
                "discard", EffectScope.Self, null, 1,
                Pile: "hand", Select: "choose"),
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5));
        var laterSideEffect = Card(
            "afterimage-later-energy",
            new CardEffect("gainEnergy", EffectScope.Self, null, 7));
        var filler = BattleFixtures.Defend();
        var hero = BattleFixtures.Hero() with
        {
            Statuses = BattleFixtures.Hero().Statuses.SetItem(
                BattleStatusIds.Afterimage, 1),
        };
        var state = BattleFixtures.MinimalState(
            allies: [hero],
            enemies: [BattleFixtures.Goblin(0, 5)],
            hand:
            [
                Instance(filler, "choice-a"),
                Instance(filler, "choice-b"),
            ]) with
        {
            Energy = 0,
            DrawPerTurn = 0,
            LastFinisherRecord =
            [
                Snapshot(lethalAfterChoice, hero.InstanceId, "lethal-used", 1),
                Snapshot(laterSideEffect, hero.InstanceId, "later-used", 2),
            ],
        };
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [lethalAfterChoice, laterSideEffect, filler]);

        var (paused, _) = BattleEngine.EndTurn(state, Rng(), catalog);
        Assert.NotNull(paused.PendingCardPlay);

        var (resolved, events) = BattleEngine.ResolveCardChoice(
            paused,
            ImmutableArray.Create("choice-a"),
            Rng(),
            catalog);

        Assert.Equal(BattleOutcome.Victory, resolved.Outcome);
        Assert.Equal(BattlePhase.Resolved, resolved.Phase);
        Assert.Equal(0, resolved.Energy);
        Assert.DoesNotContain(
            events,
            battleEvent => battleEvent.Kind == BattleEventKind.GainEnergy);
        Assert.DoesNotContain(
            events,
            battleEvent => battleEvent.Kind == BattleEventKind.ReenactCardEffects
                && battleEvent.CardId == laterSideEffect.Id);
    }

    [Fact]
    public void Before_enemy_attack_choice_resumes_current_and_remaining_enemies_once()
    {
        var contingency = ContingencyCard(
            "enemy-choice",
            ContingencyTriggerTiming.BeforeEnemyAttack);
        var filler = BattleFixtures.Defend();
        var hero = BattleFixtures.Hero();
        var enemyA = BattleFixtures.Goblin(0, 50);
        var enemyB = BattleFixtures.Goblin(1, 50) with
        {
            InstanceId = "goblin_inst_1",
        };
        var armedCard = Instance(contingency, "armed-enemy-choice");
        var state = BattleFixtures.MinimalState(
            allies: [hero],
            enemies: [enemyA, enemyB],
            hand:
            [
                Instance(filler, "choice-a"),
                Instance(filler, "choice-b"),
            ]) with
        {
            ExhaustPile = [armedCard],
            ArmedContingencies =
            [
                new ArmedContingency(
                    armedCard.InstanceId,
                    hero.InstanceId,
                    new ContingencyTriggerDefinition(
                        ContingencyTriggerTiming.BeforeEnemyAttack),
                    1),
            ],
            DrawPerTurn = 0,
        };
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [contingency, filler]);

        var (paused, firstEvents) = BattleEngine.EndTurn(
            state, Rng(), catalog);

        Assert.Equal(hero.CurrentHp, paused.Allies[0].CurrentHp);
        Assert.DoesNotContain(firstEvents, battleEvent =>
            battleEvent.Kind == BattleEventKind.DealDamage
            && battleEvent.TargetInstanceId == hero.InstanceId);
        Assert.Equal(
            TurnResolutionContinuationStage.EnemyAttacking,
            paused.PendingTurnResolution?.Stage);
        Assert.Equal(
            new[] { enemyA.InstanceId, enemyB.InstanceId },
            paused.PendingTurnResolution!
                .RemainingEnemyActorInstanceIds.AsEnumerable());

        var (resumed, resumedEvents) = BattleEngine.ResolveCardChoice(
            paused,
            ImmutableArray.Create("choice-a"),
            Rng(),
            catalog);

        Assert.Equal(BattlePhase.PlayerInput, resumed.Phase);
        Assert.Equal(2, resumed.Turn);
        Assert.Null(resumed.PendingTurnResolution);
        Assert.Equal(2, resumedEvents.Count(battleEvent =>
            battleEvent.Kind == BattleEventKind.DealDamage
            && battleEvent.TargetInstanceId == hero.InstanceId));
    }

    [Fact]
    public void Before_enemy_attack_choice_keeps_the_single_target_selected_before_summoning()
    {
        var contingency = new CardDefinition(
            "summon-before-enemy-choice",
            "summon-before-enemy-choice",
            null,
            CardRarity.Common,
            CardType.Skill,
            Cost: 0,
            UpgradedCost: null,
            Effects:
            [
                new CardEffect(
                    "summon", EffectScope.Self, null, 0,
                    UnitId: "choice-minion"),
                new CardEffect(
                    "discard", EffectScope.Self, null, 1,
                    Pile: "hand", Select: "choose"),
            ],
            UpgradedEffects: null,
            Keywords: null,
            Abilities:
            [
                new ContingencyAbilityDefinition(
                    new ContingencyTriggerDefinition(
                        ContingencyTriggerTiming.BeforeEnemyAttack)),
            ]);
        var enemyDefinition = new EnemyDefinition(
            "single-target-paused-enemy",
            "single-target-paused-enemy",
            "img_single_target_paused_enemy",
            20,
            new EnemyPool(1, EnemyTier.Weak),
            "attack",
            [
                new MoveDefinition(
                    "attack",
                    MoveKind.Attack,
                    [
                        new CardEffect(
                            "attack", EffectScope.Single,
                            EffectSide.Enemy, 5),
                    ],
                    "attack"),
            ]);
        var hero = BattleFixtures.Hero(70, slotIndex: 2);
        var enemy = BattleFixtures.Goblin() with
        {
            DefinitionId = enemyDefinition.Id,
            CurrentMoveId = "attack",
        };
        var armedCard = Instance(contingency, "armed-summon-choice");
        var filler = BattleFixtures.Defend();
        var state = BattleFixtures.MinimalState(
            allies: [hero],
            enemies: [enemy],
            hand:
            [
                Instance(filler, "choice-a"),
                Instance(filler, "choice-b"),
            ]) with
        {
            ExhaustPile = [armedCard],
            ArmedContingencies =
            [
                new ArmedContingency(
                    armedCard.InstanceId,
                    hero.InstanceId,
                    new ContingencyTriggerDefinition(
                        ContingencyTriggerTiming.BeforeEnemyAttack),
                    1),
            ],
            DrawPerTurn = 0,
        };
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [contingency, filler],
            enemies: [enemyDefinition],
            units: [BattleFixtures.MinionDef("choice-minion")]);

        var (paused, _) = BattleEngine.EndTurn(state, Rng(), catalog);

        var summoned = Assert.Single(
            paused.Allies,
            actor => actor.DefinitionId == "choice-minion");
        Assert.Equal(1, summoned.SlotIndex);
        Assert.Equal(
            hero.InstanceId,
            paused.PendingTurnResolution?
                .FirstEnemySingleTargetActorInstanceId);

        var (resumed, resumedEvents) = BattleEngine.ResolveCardChoice(
            paused,
            ImmutableArray.Create("choice-a"),
            Rng(),
            catalog);

        Assert.Equal(65, resumed.Allies.Single(actor =>
            actor.InstanceId == hero.InstanceId).CurrentHp);
        Assert.Equal(
            summoned.MaxHp,
            resumed.Allies.Single(actor =>
                actor.InstanceId == summoned.InstanceId).CurrentHp);
        Assert.Single(resumedEvents, battleEvent =>
            battleEvent.Kind == BattleEventKind.DealDamage
            && battleEvent.TargetInstanceId == hero.InstanceId);
        Assert.DoesNotContain(resumedEvents, battleEvent =>
            battleEvent.Kind == BattleEventKind.DealDamage
            && battleEvent.TargetInstanceId == summoned.InstanceId);
    }

    [Fact]
    public void Consecutive_turn_start_choices_pause_twice_without_repeating_refresh()
    {
        var contingency = ContingencyCard(
            "turn-start-choice",
            ContingencyTriggerTiming.TurnStart);
        var filler = BattleFixtures.Defend();
        var hero = BattleFixtures.Hero();
        var firstArmed = Instance(contingency, "armed-start-a");
        var secondArmed = Instance(contingency, "armed-start-b");
        var state = BattleFixtures.MinimalState(
            allies: [hero],
            enemies: [BattleFixtures.Goblin(0, 50)],
            draw:
            [
                Instance(filler, "draw-a"),
                Instance(filler, "draw-b"),
                Instance(filler, "draw-c"),
            ]) with
        {
            Energy = 0,
            EnergyMax = 3,
            DrawPerTurn = 3,
            ExhaustPile = [firstArmed, secondArmed],
            ArmedContingencies =
            [
                new ArmedContingency(
                    firstArmed.InstanceId,
                    hero.InstanceId,
                    new ContingencyTriggerDefinition(
                        ContingencyTriggerTiming.TurnStart),
                    1),
                new ArmedContingency(
                    secondArmed.InstanceId,
                    hero.InstanceId,
                    new ContingencyTriggerDefinition(
                        ContingencyTriggerTiming.TurnStart),
                    2),
            ],
        };
        var catalog = BattleFixtures.MinimalCatalog(
            cards: [contingency, filler]);

        var (firstPause, _) = BattleEngine.EndTurn(state, Rng(), catalog);

        Assert.Equal(2, firstPause.Turn);
        Assert.Equal(3, firstPause.Energy);
        Assert.Equal(
            TurnResolutionContinuationStage.TurnStartContingencies,
            firstPause.PendingTurnResolution?.Stage);
        Assert.Equal(3, firstPause.Hand.Length);
        Assert.Single(firstPause.ArmedContingencies);

        var (secondPause, _) = BattleEngine.ResolveCardChoice(
            firstPause,
            ImmutableArray.Create("draw-a"),
            Rng(),
            catalog);

        Assert.Equal(2, secondPause.Turn);
        Assert.Equal(3, secondPause.Energy);
        Assert.Equal(2, secondPause.Hand.Length);
        Assert.Empty(secondPause.ArmedContingencies);
        Assert.NotNull(secondPause.PendingCardPlay);
        Assert.Equal(
            TurnResolutionContinuationStage.TurnStartContingencies,
            secondPause.PendingTurnResolution?.Stage);
        Assert.Equal(
            new[] { "draw-b", "draw-c" },
            secondPause.PendingCardPlay!.Choice.CandidateInstanceIds.AsEnumerable());

        var (completed, events) = BattleEngine.ResolveCardChoice(
            secondPause,
            ImmutableArray.Create("draw-b"),
            Rng(),
            catalog);

        Assert.Equal(2, completed.Turn);
        Assert.Equal(3, completed.Energy);
        Assert.Equal(BattlePhase.PlayerInput, completed.Phase);
        Assert.Null(completed.PendingCardPlay);
        Assert.Null(completed.PendingTurnResolution);
        Assert.Empty(completed.ArmedContingencies);
        Assert.Contains(events, battleEvent =>
            battleEvent.Kind == BattleEventKind.TurnStart);
    }

    private static CardDefinition ContingencyCard(
        string id,
        ContingencyTriggerTiming timing) =>
        new(
            id, id, null,
            CardRarity.Common, CardType.Skill,
            Cost: 0, UpgradedCost: null,
            Effects:
            [
                new CardEffect(
                    "discard", EffectScope.Self, null, 1,
                    Pile: "hand", Select: "choose"),
            ],
            UpgradedEffects: null,
            Keywords: null,
            Abilities:
            [
                new ContingencyAbilityDefinition(
                    new ContingencyTriggerDefinition(timing)),
            ]);

    private static CardDefinition Card(
        string id,
        params CardEffect[] effects) =>
        new(
            id, id, null,
            CardRarity.Common, CardType.Attack,
            Cost: 0, UpgradedCost: null,
            Effects: effects,
            UpgradedEffects: null,
            Keywords: null);

    private static PlayedCardEffectSnapshot Snapshot(
        CardDefinition definition,
        string actorInstanceId,
        string instanceId,
        long sequence) =>
        new(
            definition.Id,
            instanceId,
            actorInstanceId,
            definition.CardType,
            definition.Cost ?? 0,
            PaidCost: 0,
            IsUpgraded: false,
            ImmutableArray<string>.Empty,
            definition.Effects.ToImmutableArray(),
            sequence);

    private static BattleCardInstance Instance(
        CardDefinition definition,
        string instanceId) =>
        new(instanceId, definition.Id, false, null);
}
