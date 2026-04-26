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

    [Fact] public void Poison_kills_all_enemies_outcome_victory()
    {
        var hero = BattleFixtures.Hero(70);
        var goblin = BattleFixtures.WithPoison(BattleFixtures.Goblin(hp: 3), 5);
        var s = State(hero, goblin);
        var (next, evs) = TurnStartProcessor.Process(s, Rng());
        Assert.Equal(RoguelikeCardGame.Core.Battle.State.BattleOutcome.Victory, next.Outcome);
        Assert.Equal(BattlePhase.Resolved, next.Phase);
        Assert.Contains(evs, e => e.Kind == BattleEventKind.BattleEnd);
    }

    [Fact] public void Poison_kills_hero_outcome_defeat()
    {
        var hero = BattleFixtures.WithPoison(BattleFixtures.Hero(hp: 2), 5);
        var s = State(hero, BattleFixtures.Goblin());
        var (next, evs) = TurnStartProcessor.Process(s, Rng());
        Assert.Equal(RoguelikeCardGame.Core.Battle.State.BattleOutcome.Defeat, next.Outcome);
        Assert.Equal(BattlePhase.Resolved, next.Phase);
        Assert.Contains(evs, e => e.Kind == BattleEventKind.BattleEnd);
    }

    [Fact] public void Outcome_confirmed_skips_energy_and_draw()
    {
        var hero = BattleFixtures.WithPoison(BattleFixtures.Hero(hp: 2), 5);
        var s = State(hero, BattleFixtures.Goblin()) with
        {
            DrawPile = ImmutableArray.Create(
                BattleFixtures.MakeBattleCard("strike", "c1"),
                BattleFixtures.MakeBattleCard("strike", "c2")),
        };
        var (next, _) = TurnStartProcessor.Process(s, Rng());
        Assert.Equal(0, next.Energy);    // EnergyMax まで回復しない
        Assert.Empty(next.Hand);          // ドローしない
    }

    [Fact] public void Targeting_auto_switch_after_poison_kill()
    {
        // 敵が複数、最内側が毒死 → TargetEnemyIndex が次の生存敵へ
        var hero = BattleFixtures.Hero();
        var goblin0 = BattleFixtures.WithPoison(BattleFixtures.Goblin(0, hp: 3), 5); // 死ぬ
        var goblin1 = BattleFixtures.Goblin(1, hp: 20);                              // 生存
        var s = State(hero, goblin0, goblin1);
        var (next, _) = TurnStartProcessor.Process(s, Rng());
        Assert.Equal(RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending, next.Outcome);
        Assert.Equal(1, next.TargetEnemyIndex);
    }

    [Fact] public void Vulnerable_decrements_by_one_per_turn()
    {
        var hero = BattleFixtures.Hero();
        var goblin = BattleFixtures.WithVulnerable(BattleFixtures.Goblin(), 3);
        var s = State(hero, goblin);
        var (next, _) = TurnStartProcessor.Process(s, Rng());
        Assert.Equal(2, next.Enemies[0].GetStatus("vulnerable"));
    }

    [Fact] public void Status_at_one_decrements_to_zero_and_emits_RemoveStatus()
    {
        var hero = BattleFixtures.Hero();
        var goblin = BattleFixtures.WithVulnerable(BattleFixtures.Goblin(), 1);
        var s = State(hero, goblin);
        var (next, evs) = TurnStartProcessor.Process(s, Rng());
        Assert.False(next.Enemies[0].Statuses.ContainsKey("vulnerable"));
        Assert.Contains(evs, e => e.Kind == BattleEventKind.RemoveStatus
                                  && e.Note == "vulnerable"
                                  && e.TargetInstanceId == goblin.InstanceId);
    }

    [Fact] public void Strength_does_not_countdown()
    {
        var hero = BattleFixtures.WithStrength(BattleFixtures.Hero(), 5);
        var s = State(hero, BattleFixtures.Goblin());
        var (next, _) = TurnStartProcessor.Process(s, Rng());
        Assert.Equal(5, next.Allies[0].GetStatus("strength"));
    }

    [Fact] public void Dexterity_does_not_countdown()
    {
        var hero = BattleFixtures.WithDexterity(BattleFixtures.Hero(), 4);
        var s = State(hero, BattleFixtures.Goblin());
        var (next, _) = TurnStartProcessor.Process(s, Rng());
        Assert.Equal(4, next.Allies[0].GetStatus("dexterity"));
    }

    [Fact] public void Poison_decrements_after_damage()
    {
        // 毒 3 ターン → ダメージ 3、その後 countdown で 2 に
        var hero = BattleFixtures.WithPoison(BattleFixtures.Hero(70), 3);
        var s = State(hero, BattleFixtures.Goblin());
        var (next, _) = TurnStartProcessor.Process(s, Rng());
        Assert.Equal(70 - 3, next.Allies[0].CurrentHp);
        Assert.Equal(2, next.Allies[0].GetStatus("poison"));
    }

    [Fact] public void Multiple_decrement_statuses_all_tick()
    {
        var hero = BattleFixtures.Hero();
        var goblin = BattleFixtures.Goblin() with
        {
            Statuses = ImmutableDictionary<string, int>.Empty
                .Add("vulnerable", 2)
                .Add("weak", 1)
                .Add("omnistrike", 3),
        };
        var s = State(hero, goblin);
        var (next, _) = TurnStartProcessor.Process(s, Rng());
        Assert.Equal(1, next.Enemies[0].GetStatus("vulnerable"));
        Assert.False(next.Enemies[0].Statuses.ContainsKey("weak")); // 1 → 0 で削除
        Assert.Equal(2, next.Enemies[0].GetStatus("omnistrike"));
    }
}
