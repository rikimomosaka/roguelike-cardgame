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

public sealed class PlayerAttackingAfterimageHookTests
{
    [Fact]
    public void Repeated_afterimage_random_ally_blocks_use_the_callers_rng_sequence()
    {
        var hero = BattleFixtures.WithStatus(
            BattleFixtures.Hero(), BattleStatusIds.Afterimage, 2);
        var ally = BattleFixtures.SummonActor(
            "ally", "minion", slotIndex: 1, hp: 10);
        var state = BattleFixtures.MinimalState(
            allies: [hero, ally],
            enemies: [BattleFixtures.Goblin(hp: 20)],
            phase: BattlePhase.PlayerAttacking) with
        {
            LastFinisherRecord =
            [
                new PlayedCardEffectSnapshot(
                    "random-block",
                    "random-block-used",
                    hero.InstanceId,
                    CardType.Skill,
                    OriginalCost: 1,
                    PaidCost: 1,
                    IsUpgraded: false,
                    Keywords: [],
                    Effects:
                    [
                        new CardEffect(
                            "block",
                            EffectScope.Random,
                            EffectSide.Ally,
                            4),
                    ],
                    PlaySequence: 1),
            ],
        };

        var (next, _, paused) = BattleEngine.ResolveAfterimageAtActorEnd(
            state,
            hero.InstanceId,
            new FakeRng([0, 1], []),
            BattleFixtures.MinimalCatalog(),
            orderStart: 0);

        Assert.False(paused);
        Assert.Equal(4, next.Allies[0].Block.Sum);
        Assert.Equal(4, next.Allies[1].Block.Sum);
        Assert.Equal(0, next.Allies[0].GetStatus(BattleStatusIds.Afterimage));
    }

    [Fact]
    public void Resolves_afterimage_after_the_actors_normal_attacks()
    {
        var hero = BattleFixtures.WithStatus(
            BattleFixtures.Hero(), BattleStatusIds.Afterimage, 1) with
        {
            AttackSingle = AttackPool.Empty.Add(2),
        };
        var state = State(hero) with
        {
            LastFinisherRecord = [AttackSnapshot(hero.InstanceId, 4)],
        };

        var (next, events) = PlayerAttackingResolver.Resolve(
            state, Rng(), BattleFixtures.MinimalCatalog());

        Assert.Equal(14, next.Enemies[0].CurrentHp);
        Assert.Equal(0, next.Allies[0].GetStatus(BattleStatusIds.Afterimage));
        var damageEvents = events
            .Where(battleEvent => battleEvent.Kind == BattleEventKind.DealDamage)
            .ToArray();
        Assert.Equal(
            new[] { 2, 4 },
            damageEvents.Select(e => e.Amount.GetValueOrDefault()));
        int replayIndex = events.ToList().FindIndex(
            battleEvent => battleEvent.Kind == BattleEventKind.ReenactCardEffects);
        Assert.InRange(replayIndex, 1, events.Count - 2);
        Assert.True(damageEvents[0].Order < events[replayIndex].Order);
        Assert.True(events[replayIndex].Order < damageEvents[1].Order);
        Assert.Equal(
            Enumerable.Range(0, events.Count),
            events.Select(battleEvent => battleEvent.Order));
    }

    [Fact]
    public void Resolves_afterimage_after_a_fixed_summon_move()
    {
        var summon = BattleFixtures.WithStatus(
            BattleFixtures.SummonActor(
                "summon", "attacker", slotIndex: 0, hp: 10,
                moveId: "strike"),
            BattleStatusIds.Afterimage,
            1);
        var state = BattleFixtures.MinimalState(
            allies: [BattleFixtures.Hero(slotIndex: 0), summon],
            enemies: [BattleFixtures.Goblin(hp: 20)],
            phase: BattlePhase.PlayerAttacking) with
        {
            LastFinisherRecord = [AttackSnapshot(summon.InstanceId, 4)],
        };
        state = BattleParticipantAccess.InitializePrimary(state);
        state = BattleParticipantAccess.Replace(
            state,
            state.Participants[0] with
            {
                ActorInstanceId = summon.InstanceId,
            });
        var unit = new UnitDefinition(
            "attacker",
            "Attacker",
            "img_attacker",
            10,
            InitialMoveId: "strike",
            Moves:
            [
                new MoveDefinition(
                    "strike",
                    MoveKind.Attack,
                    [new CardEffect(
                        "attack", EffectScope.Single, EffectSide.Enemy, 3)],
                    "strike"),
            ]);
        var catalog = BattleFixtures.MinimalCatalog(units: [unit]);

        var (next, events) = PlayerAttackingResolver.Resolve(
            state, Rng(), catalog);

        Assert.Equal(13, next.Enemies[0].CurrentHp);
        Assert.Equal(
            new[] { 3, 4 },
            events
                .Where(battleEvent => battleEvent.Kind == BattleEventKind.DealDamage)
                .Select(battleEvent => battleEvent.Amount.GetValueOrDefault()));
    }

    [Fact]
    public void Choice_pause_returns_before_the_next_actor_and_before_cleanup()
    {
        var hero = BattleFixtures.WithStatus(
            BattleFixtures.Hero(), BattleStatusIds.Afterimage, 1);
        var laterActor = BattleFixtures.SummonActor(
            "later", "minion", slotIndex: 1, hp: 10) with
        {
            AttackAll = AttackPool.Empty.Add(9),
        };
        var filler = BattleFixtures.Defend();
        var state = BattleFixtures.MinimalState(
            allies: [hero, laterActor],
            enemies: [BattleFixtures.Goblin(hp: 20)],
            phase: BattlePhase.PlayerAttacking,
            hand:
            [
                BattleFixtures.MakeBattleCard(filler.Id, "filler-1"),
                BattleFixtures.MakeBattleCard(filler.Id, "filler-2"),
            ]) with
        {
            LastFinisherRecord =
            [
                new PlayedCardEffectSnapshot(
                    "choice-source",
                    "choice-source-1",
                    hero.InstanceId,
                    CardType.Attack,
                    OriginalCost: 1,
                    PaidCost: 1,
                    IsUpgraded: false,
                    Keywords: [],
                    Effects:
                    [
                        new CardEffect(
                            "discard",
                            EffectScope.Self,
                            null,
                            1,
                            Pile: "hand",
                            Select: "choose"),
                    ],
                    PlaySequence: 1),
            ],
        };
        var catalog = BattleFixtures.MinimalCatalog(cards: [filler]);

        var (paused, events) = PlayerAttackingResolver.Resolve(
            state, Rng(), catalog);

        Assert.Equal(20, paused.Enemies[0].CurrentHp);
        Assert.Equal(9, paused.Allies[1].AttackAll.Sum);
        Assert.Equal(
            EffectExecutionKind.Afterimage,
            paused.PendingCardPlay?.EffectExecution?.Kind);
        var continuation = Assert.IsType<PendingTurnResolution>(
            paused.PendingTurnResolution);
        Assert.Equal(
            TurnResolutionContinuationStage.PlayerAttacking,
            continuation.Stage);
        Assert.Equal(
            new[] { laterActor.InstanceId },
            continuation.RemainingAllyActorInstanceIds);
        Assert.DoesNotContain(
            events,
            battleEvent => battleEvent.Kind == BattleEventKind.DealDamage);
    }

    [Fact]
    public void Resolve_remaining_uses_the_captured_membership_snapshot()
    {
        var completedActor = BattleFixtures.Hero() with
        {
            AttackAll = AttackPool.Empty.Add(2),
        };
        var remainingActor = BattleFixtures.SummonActor(
            "remaining", "minion", slotIndex: 1, hp: 10) with
        {
            AttackAll = AttackPool.Empty.Add(3),
        };
        var newlySummonedActor = BattleFixtures.SummonActor(
            "new", "minion", slotIndex: 2, hp: 10) with
        {
            AttackAll = AttackPool.Empty.Add(7),
        };
        var state = BattleFixtures.MinimalState(
            allies: [completedActor, remainingActor, newlySummonedActor],
            enemies: [BattleFixtures.Goblin(hp: 20)],
            phase: BattlePhase.PlayerAttacking) with
        {
            PendingTurnResolution = new PendingTurnResolution(
                TurnResolutionContinuationStage.PlayerAttacking)
            {
                RemainingAllyActorInstanceIds = [remainingActor.InstanceId],
            },
        };

        var (next, events) = PlayerAttackingResolver.ResolveRemaining(
            state,
            state.PendingTurnResolution.RemainingAllyActorInstanceIds,
            Rng(),
            BattleFixtures.MinimalCatalog());

        Assert.Equal(17, next.Enemies[0].CurrentHp);
        Assert.Null(next.PendingTurnResolution);
        var damage = Assert.Single(
            events,
            battleEvent => battleEvent.Kind == BattleEventKind.DealDamage);
        Assert.Equal(remainingActor.InstanceId, damage.CasterInstanceId);
    }

    [Fact]
    public void Non_participant_actor_consumes_afterimage_as_an_empty_reenactment()
    {
        var summon = BattleFixtures.WithStatus(
            BattleFixtures.SummonActor(
                "summon", "legacy-minion", slotIndex: 1, hp: 10),
            BattleStatusIds.Afterimage,
            2);
        var state = BattleFixtures.MinimalState(
            allies: [BattleFixtures.Hero(), summon],
            enemies: [BattleFixtures.Goblin(hp: 20)],
            phase: BattlePhase.PlayerAttacking);

        var (next, events) = PlayerAttackingResolver.Resolve(
            state, Rng(), BattleFixtures.MinimalCatalog());

        var survivingSummon = Assert.Single(
            next.Allies,
            actor => actor.InstanceId == summon.InstanceId);
        Assert.Equal(0, survivingSummon.GetStatus(BattleStatusIds.Afterimage));
        Assert.Contains(
            events,
            battleEvent => battleEvent.Kind == BattleEventKind.RemoveStatus
                && battleEvent.TargetInstanceId == summon.InstanceId
                && battleEvent.Note == BattleStatusIds.Afterimage);
        Assert.DoesNotContain(
            events,
            battleEvent => battleEvent.Kind == BattleEventKind.ReenactCardEffects
                && battleEvent.CasterInstanceId == summon.InstanceId);
    }

    [Fact]
    public void Stops_afterimage_cards_when_an_earlier_reenacted_attack_ends_battle()
    {
        var hero = BattleFixtures.WithStatus(
            BattleFixtures.Hero(), BattleStatusIds.Afterimage, 1);
        var state = BattleFixtures.MinimalState(
            allies: [hero],
            enemies: [BattleFixtures.Goblin(hp: 4)],
            phase: BattlePhase.PlayerAttacking,
            energy: 0) with
        {
            LastFinisherRecord =
            [
                AttackSnapshot(hero.InstanceId, 4),
                new PlayedCardEffectSnapshot(
                    "saved-side-effect",
                    "saved-side-effect-1",
                    hero.InstanceId,
                    CardType.Attack,
                    OriginalCost: 1,
                    PaidCost: 1,
                    IsUpgraded: false,
                    Keywords: [],
                    Effects:
                    [
                        new CardEffect(
                            "gainEnergy", EffectScope.Self, null, 3),
                    ],
                    PlaySequence: 2),
            ],
        };

        var (next, events) = PlayerAttackingResolver.Resolve(
            state, Rng(), BattleFixtures.MinimalCatalog());

        Assert.Equal(0, next.Enemies[0].CurrentHp);
        Assert.Equal(0, next.Energy);
        Assert.DoesNotContain(
            events,
            battleEvent => battleEvent.Kind == BattleEventKind.GainEnergy);
        Assert.Single(
            events,
            battleEvent => battleEvent.Kind == BattleEventKind.ReenactCardEffects);
    }

    [Fact]
    public void Afterimage_choice_does_not_exclude_a_waiting_source_card_from_hand()
    {
        var filler = BattleFixtures.Defend();
        var hero = BattleFixtures.WithStatus(
            BattleFixtures.Hero(), BattleStatusIds.Afterimage, 1);
        var state = BattleFixtures.MinimalState(
            allies: [hero],
            enemies: [BattleFixtures.Goblin(hp: 20)],
            phase: BattlePhase.PlayerAttacking,
            hand:
            [
                BattleFixtures.MakeBattleCard(filler.Id, "wait-source"),
                BattleFixtures.MakeBattleCard(filler.Id, "other-card"),
            ]) with
        {
            LastFinisherRecord =
            [
                new PlayedCardEffectSnapshot(
                    "wait-source-definition",
                    "wait-source",
                    hero.InstanceId,
                    CardType.Attack,
                    OriginalCost: 1,
                    PaidCost: 1,
                    IsUpgraded: false,
                    Keywords: ["wait"],
                    Effects:
                    [
                        new CardEffect(
                            "discard", EffectScope.Self, null, 1,
                            Pile: "hand", Select: "choose"),
                    ],
                    PlaySequence: 1),
            ],
        };

        var (paused, _) = PlayerAttackingResolver.Resolve(
            state, Rng(), BattleFixtures.MinimalCatalog(cards: [filler]));

        Assert.NotNull(paused.PendingCardPlay);
        Assert.Equal(
            new[] { "wait-source", "other-card" },
            paused.PendingCardPlay!.Choice.CandidateInstanceIds);
    }

    [Fact]
    public void Effect_only_execution_ignores_the_source_cards_self_lifecycle_actions()
    {
        var hero = BattleFixtures.WithStatus(
            BattleFixtures.Hero(), BattleStatusIds.Afterimage, 1);
        var state = State(hero) with
        {
            LastFinisherRecord =
            [
                new PlayedCardEffectSnapshot(
                    "legacy-exhaust-source",
                    "legacy-exhaust-source-1",
                    hero.InstanceId,
                    CardType.Attack,
                    OriginalCost: 1,
                    PaidCost: 1,
                    IsUpgraded: false,
                    Keywords: [],
                    Effects:
                    [
                        new CardEffect("exhaustSelf", EffectScope.Self, null, 0),
                    ],
                    PlaySequence: 1),
            ],
        };

        var (_, events) = PlayerAttackingResolver.Resolve(
            state, Rng(), BattleFixtures.MinimalCatalog());

        Assert.DoesNotContain(
            events,
            battleEvent => battleEvent.Kind == BattleEventKind.Exhaust);
    }

    private static BattleState State(CombatActor actor) =>
        BattleFixtures.MinimalState(
            allies: [actor],
            enemies: [BattleFixtures.Goblin(hp: 20)],
            phase: BattlePhase.PlayerAttacking);

    private static PlayedCardEffectSnapshot AttackSnapshot(
        string actorInstanceId,
        int amount) =>
        new(
            "saved-attack",
            "saved-attack-1",
            actorInstanceId,
            CardType.Attack,
            OriginalCost: 1,
            PaidCost: 1,
            IsUpgraded: false,
            Keywords: [],
            Effects:
            [
                new CardEffect(
                    "attack", EffectScope.Single, EffectSide.Enemy, amount),
            ],
            PlaySequence: 1);

    private static IRng Rng() =>
        new FakeRng(new int[32], System.Array.Empty<double>());
}
