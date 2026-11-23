using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.Projection;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public sealed class ExpandedBattleStatusTests
{
    private static CombatActor WithStatus(CombatActor actor, string id, int amount) =>
        actor with { Statuses = actor.Statuses.SetItem(id, amount) };

    private static BattleState State(
        CombatActor? hero = null,
        ImmutableArray<CombatActor>? enemies = null,
        ImmutableArray<BattleCardInstance>? hand = null,
        ImmutableArray<BattleCardInstance>? draw = null,
        int energy = 10) =>
        BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(hero ?? BattleFixtures.Hero()),
            enemies: enemies,
            hand: hand,
            draw: draw,
            energy: energy);

    private static FakeRng Rng(params int[] values) =>
        new(values, Array.Empty<double>());

    [Fact]
    public void Vigor_multiplies_attack_damage_after_strength_and_weak()
    {
        var attacker = WithStatus(
            WithStatus(WithStatus(BattleFixtures.Hero(), BattleStatusIds.Strength, 2),
                BattleStatusIds.Weak, 1),
            BattleStatusIds.Vigor, 1);

        var (target, _, _) = DealDamageHelper.Apply(
            attacker, BattleFixtures.Goblin(hp: 30), baseAmount: 10,
            scopeNote: "single", orderBase: 0);

        // floor((10 + 2) * .75) * 1.5 = 13.
        Assert.Equal(17, target.CurrentHp);
    }

    [Fact]
    public void Pursuit_is_visible_in_intent_and_resolves_once_before_planned_attack()
    {
        var hero = WithStatus(BattleFixtures.Hero(), BattleStatusIds.Pursuit, 2) with
        {
            AttackSingle = AttackPool.Empty.Add(5),
        };
        var enemies = ImmutableArray.Create(
            BattleFixtures.Goblin(0, hp: 20),
            BattleFixtures.Goblin(1, hp: 20));
        var state = State(hero, enemies) with { Phase = BattlePhase.PlayerAttacking };
        var catalog = BattleFixtures.MinimalCatalog();

        var intent = CombatIntentProjector.Project(state, hero, catalog);
        Assert.NotNull(intent);
        Assert.Equal(5, intent!.AttackSingle);
        Assert.Equal(2, intent.AttackAll);

        var (next, events) = PlayerAttackingResolver.Resolve(state, Rng(), catalog);

        Assert.Equal(13, next.Enemies[0].CurrentHp);
        Assert.Equal(18, next.Enemies[1].CurrentHp);
        Assert.Equal(1, next.Allies[0].GetStatus(BattleStatusIds.Pursuit));
        Assert.Contains(events, e =>
            e.Kind == BattleEventKind.AttackFire
            && e.Note == BattleStatusIds.Pursuit
            && e.Amount == 2);
    }

    [Fact]
    public void Turn_start_statuses_apply_in_one_deterministic_pipeline()
    {
        var hero = BattleFixtures.Hero(currentHp: 5, maxHp: 20) with
        {
            Statuses = ImmutableDictionary<string, int>.Empty
                .Add(BattleStatusIds.Regeneration, 3)
                .Add(BattleStatusIds.Growth, 2)
                .Add(BattleStatusIds.Insight, 2)
                .Add(BattleStatusIds.Haste, 2)
                .Add(BattleStatusIds.Mastery, 3)
                .Add(BattleStatusIds.Exhaustion, 1),
        };
        var draw = Enumerable.Range(0, 8)
            .Select(i => BattleFixtures.MakeBattleCard("strike", $"c{i}"))
            .ToImmutableArray();
        var state = State(hero, draw: draw, energy: 1) with { ComboGauge = 8 };

        var (next, _) = TurnStartProcessor.Process(
            state, Rng(), BattleFixtures.MinimalCatalog());

        var updated = next.Allies[0];
        Assert.Equal(8, updated.CurrentHp);
        Assert.Equal(2, updated.GetStatus(BattleStatusIds.Regeneration));
        Assert.Equal(2, updated.GetStatus(BattleStatusIds.Strength));
        Assert.Equal(0, updated.GetStatus(BattleStatusIds.Insight));
        Assert.Equal(0, updated.GetStatus(BattleStatusIds.Haste));
        Assert.Equal(2, updated.GetStatus(BattleStatusIds.Mastery));
        Assert.Equal(0, updated.GetStatus(BattleStatusIds.Exhaustion));
        Assert.Equal(10, next.ComboGauge);
        Assert.Equal(5, next.Energy);
        Assert.Equal(7, next.Hand.Length);
    }

    [Fact]
    public void Burn_can_trigger_revive_instead_of_defeat()
    {
        var hero = BattleFixtures.Hero(currentHp: 10, maxHp: 20) with
        {
            Statuses = ImmutableDictionary<string, int>.Empty
                .Add(BattleStatusIds.Burn, 200)
                .Add(BattleStatusIds.Revive, 4),
        };

        var (next, events) = TurnStartProcessor.Process(
            State(hero), Rng(), BattleFixtures.MinimalCatalog());

        Assert.Equal(4, next.Allies[0].CurrentHp);
        Assert.Equal(BattleOutcome.Pending, next.Outcome);
        Assert.Equal(0, next.Allies[0].GetStatus(BattleStatusIds.Burn));
        Assert.Equal(0, next.Allies[0].GetStatus(BattleStatusIds.Revive));
        Assert.Contains(events, e => e.Kind == BattleEventKind.Heal
            && e.Note == BattleStatusIds.Revive);
    }

    [Fact]
    public void Virulent_poison_multiplies_poison_tick()
    {
        var hero = BattleFixtures.Hero(currentHp: 30, maxHp: 30) with
        {
            Statuses = ImmutableDictionary<string, int>.Empty
                .Add(BattleStatusIds.Poison, 10)
                .Add(BattleStatusIds.VirulentPoison, 2),
        };

        var (next, events) = TurnStartProcessor.Process(
            State(hero), Rng(), BattleFixtures.MinimalCatalog());

        Assert.Equal(10, next.Allies[0].CurrentHp);
        Assert.Equal(9, next.Allies[0].GetStatus(BattleStatusIds.Poison));
        Assert.Contains(events, e =>
            e.Kind == BattleEventKind.PoisonTick && e.Amount == 20);
    }

    [Fact]
    public void Scorch_is_spent_instead_of_clearing_burn()
    {
        var hero = BattleFixtures.Hero(currentHp: 100, maxHp: 100) with
        {
            Statuses = ImmutableDictionary<string, int>.Empty
                .Add(BattleStatusIds.Burn, 10)
                .Add(BattleStatusIds.Scorch, 2),
        };

        var (first, firstEvents) = TurnStartProcessor.Process(
            State(hero), Rng(), BattleFixtures.MinimalCatalog());

        Assert.Equal(90, first.Allies[0].CurrentHp);
        Assert.Equal(10, first.Allies[0].GetStatus(BattleStatusIds.Burn));
        Assert.Equal(1, first.Allies[0].GetStatus(BattleStatusIds.Scorch));
        Assert.Contains(firstEvents, e => e.Kind == BattleEventKind.DealDamage
            && e.Note == BattleStatusIds.Burn && e.Amount == 10);

        var (second, _) = TurnStartProcessor.Process(
            first, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(81, second.Allies[0].CurrentHp);
        Assert.Equal(10, second.Allies[0].GetStatus(BattleStatusIds.Burn));
        Assert.Equal(0, second.Allies[0].GetStatus(BattleStatusIds.Scorch));

        var (third, _) = TurnStartProcessor.Process(
            second, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(73, third.Allies[0].CurrentHp);
        Assert.Equal(0, third.Allies[0].GetStatus(BattleStatusIds.Burn));
    }

    [Fact]
    public void Fortify_carries_only_its_limit_of_block()
    {
        var hero = WithStatus(BattleFixtures.Hero(), BattleStatusIds.Fortify, 4) with
        {
            Block = new BlockPool(10, 0),
        };

        var (next, _) = TurnEndProcessor.Process(
            State(hero), Rng(), BattleFixtures.MinimalCatalog());

        Assert.Equal(4, next.Allies[0].Block.Display(0));
    }

    [Fact]
    public void Resistance_negates_one_whole_debuff_application()
    {
        var hero = WithStatus(BattleFixtures.Hero(), BattleStatusIds.Resistance, 1);
        var state = State(hero);
        var effect = new CardEffect(
            "debuff", EffectScope.Self, null, 3,
            Name: BattleStatusIds.Vulnerable);

        var (next, events) = EffectApplier.Apply(
            state, hero, effect, Rng(), BattleFixtures.MinimalCatalog());

        Assert.Equal(0, next.Allies[0].GetStatus(BattleStatusIds.Vulnerable));
        Assert.Equal(0, next.Allies[0].GetStatus(BattleStatusIds.Resistance));
        Assert.Contains(events, e => e.Kind == BattleEventKind.RemoveStatus
            && e.Note == BattleStatusIds.Resistance);
    }

    [Fact]
    public void Confusion_sets_hand_entry_cost_without_changing_combo_cost()
    {
        var hero = WithStatus(BattleFixtures.Hero(), BattleStatusIds.Confusion, 2);
        var card = BattleFixtures.MakeBattleCard("strike", "drawn");
        var state = State(hero, draw: ImmutableArray.Create(card));

        var next = DrawHelper.Draw(state, 1, Rng(3), out int drawn);
        var definition = BattleFixtures.Strike();
        var cost = BattleCardCostCalculator.EvaluateForHand(
            next, next.Hand[0], definition, hero);

        Assert.Equal(1, drawn);
        Assert.Equal(3, next.Hand[0].CostOverride);
        Assert.Equal(1, cost.OriginalCost);
        Assert.Equal(3, cost.PayableCost);
    }

    [Fact]
    public void Confusion_replaces_fixed_cost_and_discards_older_card_modifiers_on_hand_entry()
    {
        var hero = WithStatus(BattleFixtures.Hero(), BattleStatusIds.Confusion, 1);
        var card = BattleFixtures.MakeBattleCard("strike", "drawn")
            .AddCostModifier(new CardCostModifier(
                -1, CardCostModifierDuration.UntilPlayed, "hand-maintenance"));
        var state = State(hero, draw: ImmutableArray.Create(card));

        var next = DrawHelper.Draw(state, 1, Rng(2), out _);

        Assert.Equal(2, next.Hand[0].CostOverride);
        Assert.Empty(next.Hand[0].EffectiveCostModifiers);
    }

    [Fact]
    public void Continuity_continues_and_reduces_an_otherwise_broken_combo()
    {
        var hero = WithStatus(BattleFixtures.Hero(), BattleStatusIds.Continuity, 2);
        var definition = new CardDefinition(
            "three", "three", null, CardRarity.Common, CardType.Skill,
            3, null,
            new[] { new CardEffect("retainSelf", EffectScope.Self, null, 0) },
            null, null);
        var card = new BattleCardInstance("three-i", definition.Id, false, null);
        var state = State(hero, hand: ImmutableArray.Create(card), energy: 5) with
        {
            ComboCount = 1,
            MaxComboThisTurn = 1,
            LastPlayedOrigCost = 1,
        };

        var (next, _) = BattleEngine.PlayCard(
            state, 0, 0, 0, Rng(),
            BattleFixtures.MinimalCatalog(cards: new[] { definition }));

        Assert.Equal(2, next.ComboCount);
        Assert.Equal(3, next.Energy);
        Assert.Equal(1, next.Allies[0].GetStatus(BattleStatusIds.Continuity));
    }

    [Fact]
    public void Stagnation_prevents_gain_and_distress_zeroes_resources_on_break()
    {
        var definition = new CardDefinition(
            "three", "three", null, CardRarity.Common, CardType.Skill,
            3, null,
            new[] { new CardEffect("retainSelf", EffectScope.Self, null, 0) },
            null, null);
        var card = new BattleCardInstance("three-i", definition.Id, false, null);
        var hero = BattleFixtures.Hero() with
        {
            Statuses = ImmutableDictionary<string, int>.Empty
                .Add(BattleStatusIds.Stagnation, 1)
                .Add(BattleStatusIds.Distress, 1),
        };
        var state = State(hero, hand: ImmutableArray.Create(card), energy: 5) with
        {
            ComboCount = 2,
            MaxComboThisTurn = 2,
            ComboGauge = 4,
            LastPlayedOrigCost = 1,
        };

        var (next, _) = BattleEngine.PlayCard(
            state, 0, 0, 0, Rng(),
            BattleFixtures.MinimalCatalog(cards: new[] { definition }));

        Assert.Equal(0, next.Energy);
        Assert.Equal(0, next.ComboGauge);
        Assert.Equal(1, next.ComboCount);
    }

    [Fact]
    public void Curse_locks_followup_cards_and_bind_silence_restrict_types()
    {
        var retainedSkill = new CardDefinition(
            "skill", "skill", null, CardRarity.Common, CardType.Skill,
            0, null,
            new[] { new CardEffect("retainSelf", EffectScope.Self, null, 0) },
            null, null);
        var skillCard = new BattleCardInstance("skill-i", retainedSkill.Id, false, null);
        var catalog = BattleFixtures.MinimalCatalog(cards: new[]
        {
            retainedSkill,
            BattleFixtures.Strike(),
        });
        var cursed = WithStatus(BattleFixtures.Hero(), BattleStatusIds.Curse, 1);
        var state = State(cursed, hand: ImmutableArray.Create(skillCard), energy: 0);
        var (locked, _) = BattleEngine.PlayCard(state, 0, 0, 0, Rng(), catalog);
        Assert.True(locked.CardUseLockedThisTurn);
        Assert.Throws<InvalidOperationException>(() =>
            BattleEngine.PlayCard(locked, 0, 0, 0, Rng(), catalog));

        var bound = State(
            WithStatus(BattleFixtures.Hero(), BattleStatusIds.Bind, 1),
            hand: ImmutableArray.Create(
                BattleFixtures.MakeBattleCard("strike", "strike-i")));
        Assert.Throws<InvalidOperationException>(() =>
            BattleEngine.PlayCard(bound, 0, 0, 0, Rng(), catalog));

        var silenced = State(
            WithStatus(BattleFixtures.Hero(), BattleStatusIds.Silence, 1),
            hand: ImmutableArray.Create(skillCard));
        Assert.Throws<InvalidOperationException>(() =>
            BattleEngine.PlayCard(silenced, 0, 0, 0, Rng(), catalog));
    }

    [Fact]
    public void Seal_rejects_character_skills()
    {
        var hero = WithStatus(BattleFixtures.Hero(), BattleStatusIds.Seal, 1);
        var state = State(hero) with { ComboGauge = 10 };

        Assert.Throws<InvalidOperationException>(() =>
            BattleEngine.UseTacticalSkill(state));
    }
}
