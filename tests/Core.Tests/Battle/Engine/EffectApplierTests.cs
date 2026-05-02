using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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

public class EffectApplierTests
{
    private static BattleState BasicState() => new(
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
        ComboCount: 0,
        LastPlayedOrigCost: null,
        NextCardComboFreePass: false,
        OwnedRelicIds: ImmutableArray<string>.Empty,
        Potions: ImmutableArray<string>.Empty,
        EncounterId: "enc_test");

    private static IRng Rng() => new FakeRng(new int[10], new double[0]);

    [Fact] public void Attack_single_adds_to_caster_AttackSingle()
    {
        var s = BasicState();
        var caster = s.Allies[0];
        var eff = new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 6);
        var (next, _) = EffectApplier.Apply(s, caster, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(6, next.Allies[0].AttackSingle.Sum);
        Assert.Equal(1, next.Allies[0].AttackSingle.AddCount);
    }

    [Fact] public void Attack_random_adds_to_caster_AttackRandom()
    {
        var s = BasicState();
        var caster = s.Allies[0];
        var eff = new CardEffect("attack", EffectScope.Random, EffectSide.Enemy, 4);
        var (next, _) = EffectApplier.Apply(s, caster, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(4, next.Allies[0].AttackRandom.Sum);
    }

    [Fact] public void Attack_all_adds_to_caster_AttackAll()
    {
        var s = BasicState();
        var caster = s.Allies[0];
        var eff = new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 3);
        var (next, _) = EffectApplier.Apply(s, caster, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(3, next.Allies[0].AttackAll.Sum);
    }

    [Fact] public void Block_self_adds_to_caster_block()
    {
        var s = BasicState();
        var caster = s.Allies[0];
        var eff = new CardEffect("block", EffectScope.Self, null, 5);
        var (next, events) = EffectApplier.Apply(s, caster, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(5, next.Allies[0].Block.Sum);
        Assert.Contains(events, e => e.Kind == BattleEventKind.GainBlock && e.Amount == 5);
    }

    [Fact] public void Unimplemented_action_is_noop()
    {
        var s = BasicState();
        var caster = s.Allies[0];
        var eff = new CardEffect("heal", EffectScope.Self, null, 10);
        var (next, events) = EffectApplier.Apply(s, caster, eff, Rng(), BattleFixtures.MinimalCatalog());
        // 状態変化なし、イベント emission なし (10.2.A スコープ外)
        Assert.Equal(s, next);
        Assert.Empty(events);
    }

    // =========================================================================
    // Phase 10.5.F: selfDamage / addCard / recoverFromDiscard / gainMaxEnergy
    //               + discard.Select 拡張
    // =========================================================================

    private static readonly IRng _rng = new SystemRng(seed: 42);
    private static readonly DataCatalog _catalog = BattleFixtures.MinimalCatalog();

    // --- selfDamage ---

    [Fact]
    public void SelfDamage_reduces_caster_hp_ignoring_block()
    {
        var hero = BattleFixtures.Hero(currentHp: 50, maxHp: 80) with
        {
            Block = BlockPool.Empty.Add(10),  // block あり、無視されるはず
        };
        var state = BattleFixtures.MakeStateWithHero(hero);
        var effect = new CardEffect("selfDamage", EffectScope.Self, null, 5);

        var (after, events) = EffectApplier.Apply(state, hero, effect, _rng, _catalog);

        var heroAfter = after.Allies.First(a => a.InstanceId == hero.InstanceId);
        Assert.Equal(45, heroAfter.CurrentHp);
        Assert.Contains(events, e => e.Kind == BattleEventKind.DealDamage
            && e.TargetInstanceId == hero.InstanceId
            && e.Amount == 5);
    }

    [Fact]
    public void SelfDamage_kills_caster_emits_actor_death()
    {
        var hero = BattleFixtures.Hero(currentHp: 3, maxHp: 80);
        var state = BattleFixtures.MakeStateWithHero(hero);
        var effect = new CardEffect("selfDamage", EffectScope.Self, null, 5);

        var (after, events) = EffectApplier.Apply(state, hero, effect, _rng, _catalog);

        var heroAfter = after.Allies.First(a => a.InstanceId == hero.InstanceId);
        Assert.Equal(0, heroAfter.CurrentHp);
        Assert.Contains(events, e => e.Kind == BattleEventKind.ActorDeath
            && e.TargetInstanceId == hero.InstanceId);
    }

    // --- addCard ---

    [Fact]
    public void AddCard_to_hand_appends_instance()
    {
        var state = BattleFixtures.MakeMinimalState();
        var effect = new CardEffect("addCard", EffectScope.Self, null, 1,
            Pile: "hand", CardRefId: "strike");

        var (after, events) = EffectApplier.Apply(
            state, state.Allies[0], effect, _rng, _catalog);

        Assert.Single(after.Hand);
        Assert.Equal("strike", after.Hand[0].CardDefinitionId);
        Assert.False(after.Hand[0].IsUpgraded);
        Assert.Contains(events, e => e.Kind == BattleEventKind.AddCard);
    }

    [Fact]
    public void AddCard_to_drawpile_inserts_at_top()
    {
        var state = BattleFixtures.MakeStateWithDrawPile(new[] { "defend" });
        var effect = new CardEffect("addCard", EffectScope.Self, null, 1,
            Pile: "draw", CardRefId: "strike");

        var (after, _) = EffectApplier.Apply(
            state, state.Allies[0], effect, _rng, _catalog);

        Assert.Equal(2, after.DrawPile.Length);
        Assert.Equal("strike", after.DrawPile[0].CardDefinitionId);
        Assert.Equal("defend", after.DrawPile[1].CardDefinitionId);
    }

    [Fact]
    public void AddCard_to_discard_appends()
    {
        var state = BattleFixtures.MakeMinimalState();
        var effect = new CardEffect("addCard", EffectScope.Self, null, 1,
            Pile: "discard", CardRefId: "strike");

        var (after, _) = EffectApplier.Apply(
            state, state.Allies[0], effect, _rng, _catalog);

        Assert.Single(after.DiscardPile);
        Assert.Equal("strike", after.DiscardPile[0].CardDefinitionId);
    }

    [Fact]
    public void AddCard_to_exhaust_appends()
    {
        var state = BattleFixtures.MakeMinimalState();
        var effect = new CardEffect("addCard", EffectScope.Self, null, 1,
            Pile: "exhaust", CardRefId: "strike");

        var (after, _) = EffectApplier.Apply(
            state, state.Allies[0], effect, _rng, _catalog);

        Assert.Single(after.ExhaustPile);
        Assert.Equal("strike", after.ExhaustPile[0].CardDefinitionId);
    }

    [Fact]
    public void AddCard_amount_n_creates_n_instances_with_unique_ids()
    {
        var state = BattleFixtures.MakeMinimalState();
        var effect = new CardEffect("addCard", EffectScope.Self, null, 3,
            Pile: "hand", CardRefId: "strike");

        var (after, _) = EffectApplier.Apply(
            state, state.Allies[0], effect, _rng, _catalog);

        Assert.Equal(3, after.Hand.Length);
        Assert.All(after.Hand, c => Assert.Equal("strike", c.CardDefinitionId));
        Assert.Equal(3, after.Hand.Select(c => c.InstanceId).Distinct().Count());
    }

    [Fact]
    public void AddCard_to_full_hand_overflows_to_discard()
    {
        var state = BattleFixtures.MakeStateWithHand(
            Enumerable.Repeat("strike", 10).ToArray());  // HandCap=10
        var effect = new CardEffect("addCard", EffectScope.Self, null, 2,
            Pile: "hand", CardRefId: "defend");

        var (after, _) = EffectApplier.Apply(
            state, state.Allies[0], effect, _rng, _catalog);

        Assert.Equal(10, after.Hand.Length);
        Assert.Equal(2, after.DiscardPile.Length);
        Assert.All(after.DiscardPile, c => Assert.Equal("defend", c.CardDefinitionId));
    }

    [Fact]
    public void AddCard_missing_card_ref_id_throws()
    {
        var state = BattleFixtures.MakeMinimalState();
        var effect = new CardEffect("addCard", EffectScope.Self, null, 1, Pile: "hand");

        Assert.Throws<InvalidOperationException>(() =>
            EffectApplier.Apply(state, state.Allies[0], effect, _rng, _catalog));
    }

    [Fact]
    public void AddCard_unknown_pile_throws()
    {
        var state = BattleFixtures.MakeMinimalState();
        var effect = new CardEffect("addCard", EffectScope.Self, null, 1,
            Pile: "bogus", CardRefId: "strike");

        Assert.Throws<InvalidOperationException>(() =>
            EffectApplier.Apply(state, state.Allies[0], effect, _rng, _catalog));
    }

    // --- recoverFromDiscard ---

    [Fact]
    public void RecoverFromDiscard_random_to_hand_moves_n_cards()
    {
        var state = BattleFixtures.MakeStateWithDiscardPile(
            new[] { "strike", "defend", "bash" });
        var effect = new CardEffect("recoverFromDiscard", EffectScope.Self, null, 2,
            Pile: "hand", Select: "random");

        var (after, events) = EffectApplier.Apply(
            state, state.Allies[0], effect, _rng, _catalog);

        Assert.Equal(2, after.Hand.Length);
        Assert.Single(after.DiscardPile);
        Assert.Contains(events, e => e.Kind == BattleEventKind.RecoverFromDiscard
            && e.Amount == 2);
    }

    [Fact]
    public void RecoverFromDiscard_all_to_hand_moves_all()
    {
        var state = BattleFixtures.MakeStateWithDiscardPile(new[] { "a", "b" });
        var effect = new CardEffect("recoverFromDiscard", EffectScope.Self, null, 99,
            Pile: "hand", Select: "all");

        var (after, _) = EffectApplier.Apply(
            state, state.Allies[0], effect, _rng, _catalog);

        Assert.Equal(2, after.Hand.Length);
        Assert.Empty(after.DiscardPile);
    }

    [Fact]
    public void RecoverFromDiscard_to_exhaust_moves_to_exhaust()
    {
        var state = BattleFixtures.MakeStateWithDiscardPile(new[] { "a", "b" });
        var effect = new CardEffect("recoverFromDiscard", EffectScope.Self, null, 1,
            Pile: "exhaust", Select: "random");

        var (after, _) = EffectApplier.Apply(
            state, state.Allies[0], effect, _rng, _catalog);

        Assert.Single(after.ExhaustPile);
        Assert.Single(after.DiscardPile);
    }

    [Fact]
    public void RecoverFromDiscard_choose_falls_back_to_random()
    {
        // Phase 10.5.M6.9: UI 選択フロー未実装のため、choose は暫定的に random と
        //  同じ挙動 (N 枚ランダム抽出) で fallback。500 エラーを避ける。
        var state = BattleFixtures.MakeStateWithDiscardPile(new[] { "a" });
        var effect = new CardEffect("recoverFromDiscard", EffectScope.Self, null, 1,
            Pile: "hand", Select: "choose");

        var (after, _) = EffectApplier.Apply(state, state.Allies[0], effect, _rng, _catalog);
        Assert.Empty(after.DiscardPile);
        Assert.Single(after.Hand);
    }

    [Fact]
    public void RecoverFromDiscard_empty_discard_returns_no_events()
    {
        var state = BattleFixtures.MakeMinimalState();
        var effect = new CardEffect("recoverFromDiscard", EffectScope.Self, null, 3,
            Pile: "hand", Select: "random");

        var (after, events) = EffectApplier.Apply(
            state, state.Allies[0], effect, _rng, _catalog);

        Assert.Empty(events);
        Assert.Empty(after.Hand);
    }

    [Fact]
    public void RecoverFromDiscard_invalid_pile_throws()
    {
        var state = BattleFixtures.MakeStateWithDiscardPile(new[] { "a" });
        var effect = new CardEffect("recoverFromDiscard", EffectScope.Self, null, 1,
            Pile: "draw", Select: "random");

        Assert.Throws<InvalidOperationException>(() =>
            EffectApplier.Apply(state, state.Allies[0], effect, _rng, _catalog));
    }

    // --- gainMaxEnergy ---

    [Fact]
    public void GainMaxEnergy_increases_energy_max_only()
    {
        var state = BattleFixtures.MakeMinimalState() with
        {
            EnergyMax = 3,
            Energy = 1,
        };
        var effect = new CardEffect("gainMaxEnergy", EffectScope.Self, null, 1);

        var (after, events) = EffectApplier.Apply(
            state, state.Allies[0], effect, _rng, _catalog);

        Assert.Equal(4, after.EnergyMax);
        Assert.Equal(1, after.Energy);
        Assert.Contains(events, e => e.Kind == BattleEventKind.GainMaxEnergy
            && e.Amount == 1);
    }

    // --- discard.Select 拡張 ---

    [Fact]
    public void Discard_select_all_discards_all_hand()
    {
        var state = BattleFixtures.MakeStateWithHand(new[] { "a", "b", "c" });
        var effect = new CardEffect("discard", EffectScope.Self, null, 99, Select: "all");

        var (after, events) = EffectApplier.Apply(
            state, state.Allies[0], effect, _rng, _catalog);

        Assert.Empty(after.Hand);
        Assert.Equal(3, after.DiscardPile.Length);
        Assert.Contains(events, e => e.Kind == BattleEventKind.Discard && e.Amount == 3);
    }

    [Fact]
    public void Discard_select_random_discards_n_random()
    {
        var state = BattleFixtures.MakeStateWithHand(new[] { "a", "b", "c" });
        var effect = new CardEffect("discard", EffectScope.Self, null, 2, Select: "random");

        var (after, _) = EffectApplier.Apply(
            state, state.Allies[0], effect, _rng, _catalog);

        Assert.Single(after.Hand);
        Assert.Equal(2, after.DiscardPile.Length);
    }

    [Fact]
    public void Discard_select_choose_falls_back_to_random()
    {
        // Phase 10.5.M6.9: UI 選択フロー未実装のため、choose は random fallback。
        var state = BattleFixtures.MakeStateWithHand(new[] { "a", "b" });
        var effect = new CardEffect("discard", EffectScope.Self, null, 1, Select: "choose");

        var (after, _) = EffectApplier.Apply(state, state.Allies[0], effect, _rng, _catalog);
        Assert.Single(after.Hand);
        Assert.Single(after.DiscardPile);
    }

    [Fact]
    public void Discard_legacy_scope_all_still_works()
    {
        // Select=null → 既存 Scope ベース挙動 (後方互換)
        var state = BattleFixtures.MakeStateWithHand(new[] { "a", "b", "c" });
        var effect = new CardEffect("discard", EffectScope.All, EffectSide.Ally, 99);

        var (after, events) = EffectApplier.Apply(
            state, state.Allies[0], effect, _rng, _catalog);

        Assert.Empty(after.Hand);
        Assert.Equal(3, after.DiscardPile.Length);
        Assert.Contains(events, e => e.Kind == BattleEventKind.Discard && e.Amount == 3);
    }

    // =========================================================================
    // Phase 10.5.D: AmountSource resolution integration tests
    // =========================================================================

    [Fact]
    public void Apply_attack_with_handCount_uses_runtime_hand_length_as_amount()
    {
        // Hand に 3 枚 → attack の amount は 3 として処理される
        var hero = BattleFixtures.Hero();
        var enemy = BattleFixtures.Goblin(hp: 20);
        var hand = ImmutableArray.Create(
            new BattleCardInstance("a", "x", false, null),
            new BattleCardInstance("b", "y", false, null),
            new BattleCardInstance("c", "z", false, null));
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero),
            enemies: ImmutableArray.Create(enemy)) with
        {
            Hand = hand,
            TargetEnemyIndex = 0,
        };
        var effect = new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 0,
            AmountSource: "handCount");

        var (after, _) = EffectApplier.Apply(state, hero, effect, _rng, _catalog);

        var heroAfter = after.Allies.First(a => a.InstanceId == hero.InstanceId);
        Assert.Equal(3, heroAfter.AttackSingle.Sum);
        Assert.Equal(1, heroAfter.AttackSingle.AddCount);
    }

    [Fact]
    public void Apply_draw_with_drawPileCount_uses_runtime_count()
    {
        // DrawPile 5 枚 → draw effect は 5 枚引く
        var hero = BattleFixtures.Hero();
        var draw = ImmutableArray.CreateRange(
            Enumerable.Range(0, 5).Select(i =>
                new BattleCardInstance($"d{i}", "x", false, null)));
        var state = BattleFixtures.MakeStateWithHero(hero) with { DrawPile = draw };
        var effect = new CardEffect("draw", EffectScope.Self, null, 0,
            AmountSource: "drawPileCount");

        var (after, _) = EffectApplier.Apply(state, hero, effect, _rng, _catalog);

        Assert.Equal(5, after.Hand.Length);
        Assert.Empty(after.DrawPile);
    }

    [Fact]
    public void Apply_with_unknown_amountSource_throws()
    {
        var hero = BattleFixtures.Hero();
        var state = BattleFixtures.MakeStateWithHero(hero);
        var effect = new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 0,
            AmountSource: "nonexistent");

        Assert.Throws<InvalidOperationException>(() =>
            EffectApplier.Apply(state, hero, effect, _rng, _catalog));
    }

    [Fact]
    public void Apply_without_amountSource_uses_amount_as_is()
    {
        // AmountSource null → 既存挙動 (Amount=5 が直接使われる)
        var hero = BattleFixtures.Hero();
        var enemy = BattleFixtures.Goblin(hp: 20);
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero),
            enemies: ImmutableArray.Create(enemy)) with
        {
            TargetEnemyIndex = 0,
        };
        var effect = new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 5);

        var (after, _) = EffectApplier.Apply(state, hero, effect, _rng, _catalog);

        var heroAfter = after.Allies.First(a => a.InstanceId == hero.InstanceId);
        Assert.Equal(5, heroAfter.AttackSingle.Sum);
    }

    [Fact]
    public void Apply_block_with_selfBlock_uses_runtime_block_value()
    {
        // 自身の block=4 → block effect は 4 ブロック追加 (合計 8 ブロック)
        var hero = BattleFixtures.Hero() with { Block = BlockPool.Empty.Add(4) };
        var state = BattleFixtures.MakeStateWithHero(hero);
        var effect = new CardEffect("block", EffectScope.Self, null, 0,
            AmountSource: "selfBlock");

        var (after, _) = EffectApplier.Apply(state, hero, effect, _rng, _catalog);

        var heroAfter = after.Allies.First(a => a.InstanceId == hero.InstanceId);
        Assert.Equal(8, heroAfter.Block.Sum);  // 既存 4 + 評価値 4
    }
}
