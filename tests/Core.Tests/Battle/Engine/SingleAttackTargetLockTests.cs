using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class SingleAttackTargetLockTests
{
    private static IRng Rng() =>
        new FakeRng(new int[20], System.Array.Empty<double>());

    private static BattleState State(
        CombatActor hero,
        BattlePhase phase,
        params CombatActor[] enemies) =>
        BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero),
            enemies: enemies.ToImmutableArray(),
            phase: phase);

    private static AttackHit Hit(int amount, string targetInstanceId) =>
        AttackHit.Unattributed(amount) with
        {
            TargetInstanceId = targetInstanceId,
        };

    [Fact]
    public void Single_attack_captures_the_selected_target_when_queued()
    {
        var first = BattleFixtures.Goblin(slotIndex: 0);
        var second = BattleFixtures.Goblin(slotIndex: 1);
        var state = State(
            BattleFixtures.Hero(), BattlePhase.PlayerInput, first, second) with
        {
            TargetEnemyIndex = 1,
        };

        var (next, _) = EffectApplier.Apply(
            state,
            state.Allies[0],
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 6),
            Rng(),
            BattleFixtures.MinimalCatalog());

        var hit = Assert.Single(next.Allies[0].AttackSingle.Hits);
        Assert.Equal(second.InstanceId, hit.TargetInstanceId);
    }

    [Fact]
    public void Cards_committed_to_different_targets_keep_their_own_targets()
    {
        var first = BattleFixtures.Goblin(slotIndex: 0);
        var second = BattleFixtures.Goblin(slotIndex: 1);
        var state = State(
            BattleFixtures.Hero(), BattlePhase.PlayerInput, first, second) with
        {
            Energy = 3,
            Hand = ImmutableArray.Create(
                BattleFixtures.MakeBattleCard("strike", "strike-a"),
                BattleFixtures.MakeBattleCard("strike", "strike-b")),
        };
        var catalog = BattleFixtures.MinimalCatalog();

        var (afterFirst, _) = BattleEngine.PlayCard(
            state, handIndex: 0, targetEnemyIndex: 0, targetAllyIndex: 0,
            Rng(), catalog);
        var (afterSecond, _) = BattleEngine.PlayCard(
            afterFirst, handIndex: 0, targetEnemyIndex: 1, targetAllyIndex: 0,
            Rng(), catalog);

        Assert.Equal(
            new[] { first.InstanceId, second.InstanceId },
            afterSecond.Allies[0].AttackSingle.Hits
                .Select(hit => hit.TargetInstanceId));

        var (resolved, _) = PlayerAttackingResolver.Resolve(
            afterSecond with { Phase = BattlePhase.PlayerAttacking },
            Rng(), catalog);

        Assert.Equal(14, resolved.Enemies[0].CurrentHp);
        Assert.Equal(14, resolved.Enemies[1].CurrentHp);
    }

    [Fact]
    public void Cpu_participant_attack_captures_its_own_projected_target()
    {
        var hero = BattleFixtures.Hero();
        var wisp = BattleFixtures.SummonActor(
            "wisp-inst", "wisp", slotIndex: 1) with
        {
            OwnerPlayerSlot = 1,
            OwnerActorInstanceId = "wisp-inst",
        };
        var first = BattleFixtures.Goblin(slotIndex: 0);
        var second = BattleFixtures.Goblin(slotIndex: 1);
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero, wisp),
            enemies: ImmutableArray.Create(first, second));
        state = BattleParticipantAccess.InitializePrimary(state);
        var cpu = BattleParticipantAccess.GetPrimary(state) with
        {
            ParticipantId = "cpu:wisp-inst",
            ActorInstanceId = wisp.InstanceId,
            ControllerKind = BattleParticipantControllerKind.Cpu,
            OwnerPlayerSlot = 1,
            TargetAllyIndex = 1,
            TargetEnemyIndex = 1,
            Energy = 1,
            Hand = ImmutableArray.Create(
                BattleFixtures.MakeBattleCard("strike", "wisp-strike")),
        };
        state = BattleParticipantAccess.Add(state, cpu);

        var (next, _) = BattleEngine.PlayCard(
            state, cpu.ParticipantId, handIndex: 0,
            targetEnemyIndex: 1, targetAllyIndex: 1,
            Rng(), BattleFixtures.MinimalCatalog());

        var queued = Assert.Single(
            next.Allies.Single(actor => actor.InstanceId == wisp.InstanceId)
                .AttackSingle.Hits);
        Assert.Equal(second.InstanceId, queued.TargetInstanceId);
        Assert.Equal(0, next.TargetEnemyIndex);
    }

    [Fact]
    public void Target_dead_before_chain_retargets_once_but_does_not_retarget_again()
    {
        var dead = BattleFixtures.Goblin(slotIndex: 0) with { CurrentHp = 0 };
        var fallback = BattleFixtures.Goblin(slotIndex: 1, hp: 5);
        var untouched = BattleFixtures.Goblin(slotIndex: 2, hp: 20);
        var hero = BattleFixtures.Hero() with
        {
            AttackSingle = AttackPool.Empty
                .Add(Hit(5, dead.InstanceId))
                .Add(Hit(9, dead.InstanceId)),
        };
        var state = State(
            hero, BattlePhase.PlayerAttacking, dead, fallback, untouched) with
        {
            TargetEnemyIndex = 0,
        };

        var (next, events) = PlayerAttackingResolver.Resolve(
            state, Rng(), BattleFixtures.MinimalCatalog());

        Assert.Equal(0, next.Enemies[1].CurrentHp);
        Assert.Equal(20, next.Enemies[2].CurrentHp);
        Assert.Single(events, battleEvent =>
            battleEvent.Kind == BattleEventKind.AttackFire);
    }

    [Fact]
    public void Dead_target_fizzles_only_its_locked_hits_and_later_target_still_resolves()
    {
        var first = BattleFixtures.Goblin(slotIndex: 0, hp: 5);
        var second = BattleFixtures.Goblin(slotIndex: 1, hp: 20);
        var hero = BattleFixtures.Hero() with
        {
            AttackSingle = AttackPool.Empty
                .Add(Hit(5, first.InstanceId))
                .Add(Hit(9, first.InstanceId))
                .Add(Hit(4, second.InstanceId)),
        };

        var (next, events) = PlayerAttackingResolver.Resolve(
            State(hero, BattlePhase.PlayerAttacking, first, second),
            Rng(), BattleFixtures.MinimalCatalog());

        Assert.Equal(0, next.Enemies[0].CurrentHp);
        Assert.Equal(16, next.Enemies[1].CurrentHp);
        Assert.Equal(
            new[] { first.InstanceId, second.InstanceId },
            events
                .Where(battleEvent =>
                    battleEvent.Kind == BattleEventKind.AttackFire)
                .Select(battleEvent => battleEvent.TargetInstanceId));
    }

    [Fact]
    public void Locked_target_participates_in_pool_equality_and_json_round_trip()
    {
        var first = AttackPool.Empty.Add(Hit(3, "enemy-a"));
        var second = AttackPool.Empty.Add(Hit(3, "enemy-b"));

        Assert.NotEqual(first, second);

        var restored = JsonSerializer.Deserialize<AttackPool>(
            JsonSerializer.Serialize(first));
        Assert.Equal(first, restored);
        Assert.Equal("enemy-a", Assert.Single(restored.Hits).TargetInstanceId);
    }
}
