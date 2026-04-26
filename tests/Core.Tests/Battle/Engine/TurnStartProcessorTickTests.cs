using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class TurnStartProcessorTickTests
{
    private static BattleState State(CombatActor hero, params CombatActor[] enemies) => new(
        Turn: 1, Phase: BattlePhase.PlayerInput,
        Outcome: RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending,
        Allies: ImmutableArray.Create(hero),
        Enemies: enemies.ToImmutableArray(),
        TargetAllyIndex: 0, TargetEnemyIndex: 0,
        Energy: 0, EnergyMax: 3,
        DrawPile: ImmutableArray<BattleCardInstance>.Empty,
        Hand: ImmutableArray<BattleCardInstance>.Empty,
        DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
        ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
        EncounterId: "enc_test");

    private static IRng Rng() => new FakeRng(new int[0], new double[0]);

    [Fact] public void Poison_damages_target_ignoring_block()
    {
        var hero = BattleFixtures.WithPoison(BattleFixtures.Hero(70), 3) with { Block = BlockPool.Empty.Add(10) };
        var s = State(hero, BattleFixtures.Goblin());
        var (next, evs) = TurnStartProcessor.Process(s, Rng());
        Assert.Equal(70 - 3, next.Allies[0].CurrentHp);
        Assert.Equal(10, next.Allies[0].Block.Sum); // Block は無傷
        Assert.Contains(evs, e => e.Kind == BattleEventKind.PoisonTick && e.Amount == 3);
    }

    [Fact] public void Poison_damages_all_actors_with_poison()
    {
        var hero = BattleFixtures.WithPoison(BattleFixtures.Hero(70), 2);
        var goblin0 = BattleFixtures.WithPoison(BattleFixtures.Goblin(0, hp: 20), 5);
        var goblin1 = BattleFixtures.Goblin(1, hp: 20); // poison なし
        var s = State(hero, goblin0, goblin1);
        var (next, _) = TurnStartProcessor.Process(s, Rng());
        Assert.Equal(70 - 2, next.Allies[0].CurrentHp);
        Assert.Equal(20 - 5, next.Enemies[0].CurrentHp);
        Assert.Equal(20, next.Enemies[1].CurrentHp);
    }

    [Fact] public void Dead_actor_skipped_in_poison_tick()
    {
        var hero = BattleFixtures.WithPoison(BattleFixtures.Hero(70), 2);
        var goblin = BattleFixtures.WithPoison(BattleFixtures.Goblin(hp: 20), 99) with { CurrentHp = 0 };
        var s = State(hero, goblin);
        var (next, evs) = TurnStartProcessor.Process(s, Rng());
        Assert.Equal(70 - 2, next.Allies[0].CurrentHp);
        Assert.Equal(0, next.Enemies[0].CurrentHp); // 不変
        Assert.DoesNotContain(evs, e => e.Kind == BattleEventKind.PoisonTick && e.TargetInstanceId == goblin.InstanceId);
    }

    [Fact] public void No_poison_tick_when_no_status()
    {
        var hero = BattleFixtures.Hero(70);
        var s = State(hero, BattleFixtures.Goblin());
        var (next, evs) = TurnStartProcessor.Process(s, Rng());
        Assert.Equal(70, next.Allies[0].CurrentHp);
        Assert.DoesNotContain(evs, e => e.Kind == BattleEventKind.PoisonTick);
    }

    [Fact] public void Energy_and_draw_still_happen_after_poison()
    {
        var hero = BattleFixtures.WithPoison(BattleFixtures.Hero(70), 2);
        var s = State(hero, BattleFixtures.Goblin()) with
        {
            DrawPile = ImmutableArray.Create(
                BattleFixtures.MakeBattleCard("strike", "c1"),
                BattleFixtures.MakeBattleCard("strike", "c2"),
                BattleFixtures.MakeBattleCard("strike", "c3"),
                BattleFixtures.MakeBattleCard("strike", "c4"),
                BattleFixtures.MakeBattleCard("strike", "c5")),
        };
        var (next, _) = TurnStartProcessor.Process(s, Rng());
        Assert.Equal(3, next.Energy); // EnergyMax = 3
        Assert.Equal(5, next.Hand.Length);
    }
}
