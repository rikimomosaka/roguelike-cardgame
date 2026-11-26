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
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0,
            LastPlayedOrigCost: null,
            NextCardComboFreePass: false,
            OwnedRelicIds: ImmutableArray<string>.Empty,
            Potions: ImmutableArray<string>.Empty,
            EncounterId: "enc_test");

    private static IRng Rng(params int[] ints) => new FakeRng(ints, new double[0]);

    [Fact] public void All_enemies_dead_yields_Victory()
    {
        var hero = BattleFixtures.Hero() with { AttackSingle = AttackPool.Empty.Add(99) };
        var s = WithActiveCombo(
            MakeState(hero, ImmutableArray.Create(BattleFixtures.Goblin(0, 5))));
        var cat = BattleFixtures.MinimalCatalog();
        var (next, events) = BattleEngine.EndTurn(s, Rng(), cat);
        Assert.Equal(BattleOutcome.Victory, next.Outcome);
        Assert.Equal(BattlePhase.Resolved, next.Phase);
        var battleEnd = Assert.Single(events, e => e.Kind == BattleEventKind.BattleEnd);
        Assert.Equal(events.Count - 1, battleEnd.Order);
        AssertComboReset(next);
    }

    [Fact] public void Hero_killed_yields_Defeat()
    {
        var hero = BattleFixtures.Hero(hp: 2); // 2 HP
        var s = WithActiveCombo(
            MakeState(hero, ImmutableArray.Create(BattleFixtures.Goblin())));
        var cat = BattleFixtures.MinimalCatalog(
            enemies: new[] { BattleFixtures.GoblinDef(hp: 20, attack: 5) });
        var (next, events) = BattleEngine.EndTurn(s, Rng(), cat);
        Assert.Equal(BattleOutcome.Defeat, next.Outcome);
        Assert.Equal(BattlePhase.Resolved, next.Phase);
        var battleEnd = Assert.Single(events, e => e.Kind == BattleEventKind.BattleEnd);
        Assert.Equal(events.Count - 1, battleEnd.Order);
        AssertComboReset(next);
    }

    [Fact]
    public void Dead_hero_yields_Defeat_even_when_an_allied_summon_is_alive()
    {
        var hero = BattleFixtures.Hero(hp: 10) with { CurrentHp = 0 };
        var summon = BattleFixtures.SummonActor("token-1", "wisp", slotIndex: 1, hp: 10);
        var state = MakeState(
            hero,
            ImmutableArray.Create(BattleFixtures.Goblin())) with
        {
            Allies = ImmutableArray.Create(hero, summon),
        };

        var (next, events) = BattleEngine.EndTurn(
            state, Rng(), BattleFixtures.MinimalCatalog());

        Assert.Equal(BattleOutcome.Defeat, next.Outcome);
        Assert.Equal(BattlePhase.Resolved, next.Phase);
        Assert.Contains(next.Allies, ally => ally.DefinitionId == "wisp" && ally.IsAlive);
        Assert.Equal(events.Count - 1, Assert.Single(events, e => e.Kind == BattleEventKind.BattleEnd).Order);
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
        Assert.Equal(3, next.Energy); // 持越し 0 + ターン開始時追加 3
    }

    [Fact]
    public void EndTurn_refills_energy_up_to_the_per_turn_amount_and_redraws_hand()
    {
        var oldHand = BattleFixtures.MakeBattleCard("strike", "old-hand");
        var state = MakeState(
            BattleFixtures.Hero(),
            ImmutableArray.Create(BattleFixtures.Goblin(0, 50))) with
        {
            Energy = 2,
            EnergyMax = 3,
            Hand = ImmutableArray.Create(oldHand),
        };

        var (next, _) = BattleEngine.EndTurn(
            state, Rng(), BattleFixtures.MinimalCatalog());

        Assert.Equal(BattleOutcome.Pending, next.Outcome);
        Assert.Equal(2, next.Turn);
        // 残り 2 → 規定値 3 まで回復（足し算ではない）。
        Assert.Equal(3, next.Energy);
        Assert.Equal(5, next.Hand.Length);
        Assert.Contains(next.DiscardPile, card => card.InstanceId == oldHand.InstanceId);
        Assert.DoesNotContain(next.Hand, card => card.InstanceId == oldHand.InstanceId);
    }

    [Fact]
    public void OnTurnEnd_energy_gain_counts_toward_the_refill_target()
    {
        var relic = BattleFixtures.Relic(
            "end-energy", "OnTurnEnd", true,
            new CardEffect("gainEnergy", EffectScope.Self, null, 1));
        var catalog = BattleFixtures.MinimalCatalog(relics: new[] { relic });
        var state = MakeState(
            BattleFixtures.Hero(),
            ImmutableArray.Create(BattleFixtures.Goblin(0, 50))) with
        {
            Energy = 2,
            EnergyMax = 3,
            OwnedRelicIds = ImmutableArray.Create("end-energy"),
        };

        var (next, _) = BattleEngine.EndTurn(state, Rng(), catalog);

        Assert.Equal(BattleOutcome.Pending, next.Outcome);
        Assert.Equal(3, next.Energy); // 未使用2 + OnTurnEnd 1 = 3 → 規定値 3 以上なのでそのまま
    }

    [Fact]
    public void EndTurn_keeps_energy_above_the_per_turn_amount_without_adding()
    {
        var state = MakeState(
            BattleFixtures.Hero(),
            ImmutableArray.Create(BattleFixtures.Goblin(0, 50))) with
        {
            Energy = 5,
            EnergyMax = 3,
        };

        var (next, _) = BattleEngine.EndTurn(state, Rng(), BattleFixtures.MinimalCatalog());

        Assert.Equal(5, next.Energy);
    }

    [Fact]
    public void EndTurn_with_zero_energy_refills_to_the_per_turn_amount()
    {
        var state = MakeState(
            BattleFixtures.Hero(),
            ImmutableArray.Create(BattleFixtures.Goblin(0, 50))) with
        {
            Energy = 0,
            EnergyMax = 4,
        };

        var (next, _) = BattleEngine.EndTurn(state, Rng(), BattleFixtures.MinimalCatalog());

        Assert.Equal(4, next.Energy);
    }

    [Fact]
    public void Played_multi_hit_card_resolves_as_separate_hits_at_turn_end()
    {
        var doubleHit = new CardDefinition(
            "double_hit", "Double Hit", null, CardRarity.Common, CardType.Attack,
            Cost: 1, UpgradedCost: null,
            Effects: new CardEffect[]
            {
                new("attack", EffectScope.Single, EffectSide.Enemy, 5),
                new("attack", EffectScope.Single, EffectSide.Enemy, 5),
            },
            UpgradedEffects: null, Keywords: null);
        var card = BattleFixtures.MakeBattleCard("double_hit", "double_hit_inst");
        var enemy = BattleFixtures.Goblin(hp: 50) with
        {
            Block = BlockPool.Empty.Add(6),
        };
        var state = MakeState(
            BattleFixtures.Hero(), ImmutableArray.Create(enemy),
            draw: ImmutableArray<BattleCardInstance>.Empty) with
        {
            Energy = 3,
            Hand = ImmutableArray.Create(card),
        };
        var catalog = BattleFixtures.MinimalCatalog(cards: new[] { doubleHit });
        var rng = Rng();

        var (afterPlay, _) = BattleEngine.PlayCard(
            state, handIndex: 0, targetEnemyIndex: 0, targetAllyIndex: 0,
            rng, catalog);
        Assert.Equal(new[] { 5, 5 }, afterPlay.Allies[0].AttackSingle.HitAmounts);

        var (afterTurn, events) = BattleEngine.EndTurn(afterPlay, rng, catalog);

        Assert.Equal(46, afterTurn.Enemies[0].CurrentHp);
        var heroDamageEvents = events
            .Where(e => e.Kind == BattleEventKind.DealDamage
                && e.CasterInstanceId == afterPlay.Allies[0].InstanceId)
            .ToList();
        Assert.Equal(new int?[] { 0, 4 }, heroDamageEvents.Select(e => e.Amount));
        Assert.Equal(new int?[] { 5, 1 }, heroDamageEvents.Select(e => e.BlockedAmount));
        Assert.Equal(new int?[] { 0, 1 }, heroDamageEvents.Select(e => e.HitIndex));
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

    [Fact] public void EndTurn_resets_combo_fields_after_TurnEnd()
    {
        // Build a state mid-combo (simulating SuperWild played, FreePass active),
        // then call EndTurn → PlayerAttacking → EnemyAttacking → TurnEndProcessor → TurnStartProcessor.
        // After EndTurn returns (Outcome=Pending), all combo fields should be reset.
        var s = MakeStateWithCombo(
            combo: 3, lastOrigCost: 5, freePass: true, wildUsed: true);
        var cat = BattleFixtures.MinimalCatalog();

        var (next, _) = BattleEngine.EndTurn(s, Rng(), cat);

        // PlayerAttacking + EnemyAttacking + TurnEndProcessor + TurnStartProcessor
        // Precondition: this fixture must not end combat — otherwise the combo-reset
        // invariant we want to verify never triggers.
        Assert.Equal(BattleOutcome.Pending, next.Outcome);

        Assert.Equal(0, next.ComboCount);
        Assert.Equal(0, next.MaxComboThisTurn);
        Assert.Null(next.LastPlayedOrigCost);
        Assert.False(next.NextCardComboFreePass);
        Assert.False(next.WildUsedInCurrentCombo);
    }

    private static BattleState WithActiveCombo(BattleState state) => state with
    {
        ComboCount = 3,
        MaxComboThisTurn = 5,
        LastPlayedOrigCost = 2,
        NextCardComboFreePass = true,
        WildUsedInCurrentCombo = true,
    };

    private static void AssertComboReset(BattleState state)
    {
        Assert.Equal(0, state.ComboCount);
        Assert.Equal(0, state.MaxComboThisTurn);
        Assert.Null(state.LastPlayedOrigCost);
        Assert.False(state.NextCardComboFreePass);
        Assert.False(state.WildUsedInCurrentCombo);
    }

    private static BattleState MakeStateWithCombo(
        int combo, int? lastOrigCost, bool freePass, bool wildUsed)
    {
        return new(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: ImmutableArray.Create(BattleFixtures.Hero()),
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: 3, EnergyMax: 3,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: combo,
            LastPlayedOrigCost: lastOrigCost,
            NextCardComboFreePass: freePass,
            OwnedRelicIds: ImmutableArray<string>.Empty,
            Potions: ImmutableArray<string>.Empty,
            EncounterId: "enc_test",
            WildUsedInCurrentCombo: wildUsed);
    }
}
