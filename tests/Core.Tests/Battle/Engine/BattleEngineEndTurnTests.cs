using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class BattleEngineEndTurnTests
{
    private static BattleState MakeState(
        CombatActor hero,
        ImmutableArray<CombatActor> enemies,
        ImmutableArray<BattleCardInstance>? draw = null)
        => new(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(hero),
            Enemies: enemies,
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: 0, EnergyMax: 3,
            DrawPile: draw ?? Enumerable.Range(0, 5)
                .Select(i => BattleFixtures.MakeBattleCard("strike", $"c{i}"))
                .ToImmutableArray(),
            Hand: ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            EncounterId: "enc_test");

    private static IRng Rng(params int[] ints) => new FakeRng(ints, new double[0]);

    [Fact] public void All_enemies_dead_yields_Victory()
    {
        var hero = BattleFixtures.Hero() with { AttackSingle = AttackPool.Empty.Add(99) };
        var s = MakeState(hero, ImmutableArray.Create(BattleFixtures.Goblin(0, 5)));
        var cat = BattleFixtures.MinimalCatalog();
        var (next, events) = BattleEngine.EndTurn(s, Rng(), cat);
        Assert.Equal(BattleOutcome.Victory, next.Outcome);
        Assert.Equal(BattlePhase.Resolved, next.Phase);
        Assert.Contains(events, e => e.Kind == BattleEventKind.BattleEnd);
    }

    [Fact] public void Hero_killed_yields_Defeat()
    {
        var hero = BattleFixtures.Hero(hp: 2); // 2 HP
        var s = MakeState(hero, ImmutableArray.Create(BattleFixtures.Goblin()));
        var cat = BattleFixtures.MinimalCatalog(
            enemies: new[] { BattleFixtures.GoblinDef(hp: 20, attack: 5) });
        var (next, events) = BattleEngine.EndTurn(s, Rng(), cat);
        Assert.Equal(BattleOutcome.Defeat, next.Outcome);
        Assert.Equal(BattlePhase.Resolved, next.Phase);
        Assert.Contains(events, e => e.Kind == BattleEventKind.BattleEnd);
    }

    [Fact] public void Continues_to_next_turn_when_neither_side_dies()
    {
        var hero = BattleFixtures.Hero();
        var goblin = BattleFixtures.Goblin(0, 50); // tough
        var s = MakeState(hero, ImmutableArray.Create(goblin));
        var cat = BattleFixtures.MinimalCatalog();
        var (next, _) = BattleEngine.EndTurn(s, Rng(), cat);
        Assert.Equal(BattlePhase.PlayerInput, next.Phase);
        Assert.Equal(BattleOutcome.Pending, next.Outcome);
        Assert.Equal(2, next.Turn);
        Assert.Equal(3, next.Energy); // refilled
    }

    [Fact] public void Hand_discarded_and_redrawn()
    {
        var hero = BattleFixtures.Hero();
        var hand = ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "h1"));
        var s = MakeState(hero, ImmutableArray.Create(BattleFixtures.Goblin(0, 50))) with
        {
            Hand = hand
        };
        var cat = BattleFixtures.MinimalCatalog();
        var (next, _) = BattleEngine.EndTurn(s, Rng(), cat);
        // h1 が捨てられ、5 枚新規ドロー
        Assert.Equal(5, next.Hand.Length);
        Assert.Contains(next.DiscardPile, c => c.InstanceId == "h1");
    }

    [Fact] public void Throws_when_not_PlayerInput()
    {
        var hero = BattleFixtures.Hero();
        var s = MakeState(hero, ImmutableArray.Create(BattleFixtures.Goblin())) with
        {
            Phase = BattlePhase.EnemyAttacking
        };
        var cat = BattleFixtures.MinimalCatalog();
        Assert.Throws<System.InvalidOperationException>(() =>
            BattleEngine.EndTurn(s, Rng(), cat));
    }

    [Fact] public void EndTurn_with_poison_dying_hero_at_next_turn_keeps_resolved_phase()
    {
        // hero に毒(5)を付ける。HP=2 なので次ターン TurnStart の毒 tick で確実に死亡
        var hero = BattleFixtures.WithPoison(BattleFixtures.Hero(hp: 2), 5);
        var goblin = BattleFixtures.Goblin(hp: 100, moveId: "noop");

        // 敵 move が noop（攻撃なし）→ EndTurn 中は hero 生存、TurnStart で毒死
        var noopMove = new MoveDefinition("noop", MoveKind.Unknown, System.Array.Empty<CardEffect>(), "noop");
        var goblinDef = new EnemyDefinition("goblin", "Goblin", "img_goblin",
            100, new EnemyPool(1, EnemyTier.Weak), "noop", new[] { noopMove });

        var cards = new[] { BattleFixtures.Strike() };
        var enemies = new[] { goblinDef };
        var encs = new[] { new EncounterDefinition("enc_test", new EnemyPool(1, EnemyTier.Weak), new[] { "goblin" }) };
        var catalog = BattleFixtures.MinimalCatalog(cards, enemies, encs);

        var s = MakeState(hero, ImmutableArray.Create(goblin)) with
        {
            Phase = BattlePhase.PlayerInput,
            DrawPile = ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "c1")),
        };
        var rng = new FakeRng(new int[0], new double[0]);
        var (next, _) = BattleEngine.EndTurn(s, rng, catalog);

        Assert.Equal(RoguelikeCardGame.Core.Battle.State.BattleOutcome.Defeat, next.Outcome);
        Assert.Equal(BattlePhase.Resolved, next.Phase); // PlayerInput に上書きされていない
    }
}
