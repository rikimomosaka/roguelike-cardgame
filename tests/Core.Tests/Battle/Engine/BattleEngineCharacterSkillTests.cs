using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class BattleEngineCharacterSkillTests
{
    private static IRng Rng() => new FakeRng(new int[100], Array.Empty<double>());

    private static CardDefinition Card(string id, int cost = 1) =>
        new(
            id, id, null, CardRarity.Common, CardType.Skill,
            Cost: cost, UpgradedCost: null,
            Effects: new[] { new CardEffect("block", EffectScope.Self, null, 1) },
            UpgradedEffects: null, Keywords: null);

    private static CardDefinition ChooseCard(string id = "choose") =>
        new(
            id, id, null, CardRarity.Common, CardType.Skill,
            Cost: 0, UpgradedCost: null,
            Effects: new[]
            {
                new CardEffect(
                    "exhaustCard", EffectScope.Self, null, 1,
                    Pile: "hand", Select: "choose"),
            },
            UpgradedEffects: null, Keywords: null);

    private static BattleState State(
        ImmutableArray<BattleCardInstance> hand,
        int gauge = 0,
        int combo = 0,
        int maxCombo = 0,
        int? lastCost = null,
        int energy = 20) =>
        BattleFixtures.MinimalState(hand: hand, energy: energy, energyMax: 3) with
        {
            ComboGauge = gauge,
            ComboCount = combo,
            MaxComboThisTurn = maxCombo,
            LastPlayedOrigCost = lastCost,
        };

    private static DataCatalog Catalog(params CardDefinition[] cards) =>
        BattleFixtures.MinimalCatalog(cards: cards);

    [Fact]
    public void UseCharacterSkill_reads_character_definition_and_applies_card_effects()
    {
        var def = Card("c1");
        var character = new CharacterDefinition(
            "default", "default", 50, 0, 0, new[] { def.Id },
            Skills: new[]
            {
                new CharacterSkillDefinition(
                    "custom", "Custom", new ResourceCost(Energy: 2, ComboGauge: 2),
                    OncePerTurn: true,
                    Effects: new[]
                    {
                        new CardEffect("buff", EffectScope.Self, null, 2, Name: "strength"),
                    }),
            });
        var catalog = Catalog(def) with
        {
            Characters = new System.Collections.Generic.Dictionary<string, CharacterDefinition>
            {
                [character.Id] = character,
            },
        };
        var state = State(ImmutableArray.Create(new BattleCardInstance("card-1", def.Id, false, null)),
            gauge: 3, energy: 5) with { CharacterId = character.Id };

        var (next, events) = BattleEngine.UseCharacterSkill(
            state, "custom", catalog, Rng());

        Assert.Equal(3, next.Energy);
        Assert.Equal(1, next.ComboGauge);
        Assert.Equal(2, next.Allies[0].GetStatus("strength"));
        Assert.Contains("custom", next.UsedCharacterSkillIdsThisTurn);
        Assert.Contains(events, e => e.Kind == BattleEventKind.ApplyStatus && e.Note == "strength");
    }

    [Fact]
    public void Data_driven_skill_uses_configured_number_of_times_per_turn()
    {
        var def = Card("c1");
        var character = new CharacterDefinition(
            "default", "default", 50, 0, 0, new[] { def.Id },
            Skills: new[]
            {
                new CharacterSkillDefinition(
                    "technique", "Technique", new ResourceCost(ComboGauge: 1),
                    OncePerTurn: false,
                    Effects: new[]
                    {
                        new CardEffect("buff", EffectScope.Self, null, 1, Name: "strength"),
                    },
                    UsesPerTurn: 2),
            });
        var catalog = Catalog(def) with
        {
            Characters = new System.Collections.Generic.Dictionary<string, CharacterDefinition>
            {
                [character.Id] = character,
            },
        };
        var state = State(ImmutableArray.Create(new BattleCardInstance("card-1", def.Id, false, null)),
            gauge: 3) with { CharacterId = character.Id };

        var (afterFirst, _) = BattleEngine.UseCharacterSkill(state, "technique", catalog, Rng());
        var (afterSecond, _) = BattleEngine.UseCharacterSkill(afterFirst, "technique", catalog, Rng());

        Assert.Equal(1, afterSecond.ComboGauge);
        Assert.Equal(2, afterSecond.CharacterSkillUsesThisTurn["technique"]);
        Assert.Throws<InvalidOperationException>(() =>
            BattleEngine.UseCharacterSkill(afterSecond, "technique", catalog, Rng()));
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) UseUltimate(
        BattleState state, DataCatalog? catalog = null)
    {
        catalog ??= Catalog(state.Hand
            .Concat(state.DrawPile)
            .Concat(state.DiscardPile)
            .Concat(state.ExhaustPile)
            .Concat(state.SummonHeld)
            .Concat(state.PowerCards)
            .Select(card => card.CardDefinitionId)
            .Distinct(StringComparer.Ordinal)
            .Select(id => Card(id))
            .ToArray());
        return BattleEngine.UseUltimateSkill(state, catalog);
    }

    private static System.Collections.Generic.IEnumerable<BattleCardInstance> AllCards(
        BattleState state) => state.DrawPile
        .Concat(state.Hand)
        .Concat(state.DiscardPile)
        .Concat(state.ExhaustPile)
        .Concat(state.SummonHeld)
        .Concat(state.PowerCards);

    [Fact]
    public void PlayCard_adds_only_new_turn_maximum_difference_to_gauge()
    {
        var def = Card("c1");
        var state = State(
            ImmutableArray.Create(new BattleCardInstance("card-1", def.Id, false, null)),
            gauge: 2,
            combo: 4,
            maxCombo: 4,
            lastCost: 0);

        var (next, events) = BattleEngine.PlayCard(
            state, 0, 0, 0, Rng(), Catalog(def));

        Assert.Equal(5, next.ComboCount);
        Assert.Equal(5, next.MaxComboThisTurn);
        Assert.Equal(3, next.ComboGauge);
        var changed = Assert.Single(events, e => e.Kind == BattleEventKind.ComboGaugeChanged);
        Assert.Equal(1, changed.Amount);
        Assert.Equal("remaining=3", changed.Note);
        Assert.Equal(1, changed.Order);
        Assert.Equal(CombatSourceKind.CardEffect, changed.Source!.Kind);
    }

    [Fact]
    public void PlayCard_does_not_charge_until_combo_exceeds_earlier_turn_maximum()
    {
        var def = Card("c1");
        var state = State(
            ImmutableArray.Create(new BattleCardInstance("card-1", def.Id, false, null)),
            gauge: 4,
            combo: 1,
            maxCombo: 5,
            lastCost: 0);

        var (next, events) = BattleEngine.PlayCard(
            state, 0, 0, 0, Rng(), Catalog(def));

        Assert.Equal(2, next.ComboCount);
        Assert.Equal(5, next.MaxComboThisTurn);
        Assert.Equal(4, next.ComboGauge);
        Assert.DoesNotContain(events, e => e.Kind == BattleEventKind.ComboGaugeChanged);
    }

    [Fact]
    public void PlayCard_combo_gauge_saturates_at_ten_and_reports_actual_gain()
    {
        var def = Card("c1");
        var state = State(
            ImmutableArray.Create(new BattleCardInstance("card-1", def.Id, false, null)),
            gauge: 9,
            combo: 3,
            maxCombo: 3,
            lastCost: 0);

        var (next, events) = BattleEngine.PlayCard(
            state, 0, 0, 0, Rng(), Catalog(def));

        Assert.Equal(BattleEngine.MaxComboGauge, next.ComboGauge);
        var changed = Assert.Single(events, e => e.Kind == BattleEventKind.ComboGaugeChanged);
        Assert.Equal(1, changed.Amount);
        Assert.Equal("remaining=10", changed.Note);
    }

    [Fact]
    public void PlayCard_at_full_gauge_does_not_emit_zero_change_event()
    {
        var def = Card("c1");
        var state = State(
            ImmutableArray.Create(new BattleCardInstance("card-1", def.Id, false, null)),
            gauge: 10,
            combo: 1,
            maxCombo: 1,
            lastCost: 0);

        var (next, events) = BattleEngine.PlayCard(
            state, 0, 0, 0, Rng(), Catalog(def));

        Assert.Equal(10, next.ComboGauge);
        Assert.Equal(2, next.MaxComboThisTurn);
        Assert.DoesNotContain(events, e => e.Kind == BattleEventKind.ComboGaugeChanged);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(1, 0, 0)]
    [InlineData(0, 0, -1)]
    [InlineData(0, 0, 11)]
    public void PlayCard_rejects_invalid_combo_tracking_state(
        int combo, int maxCombo, int gauge)
    {
        var def = Card("c1");
        var state = State(
            ImmutableArray.Create(new BattleCardInstance("card-1", def.Id, false, null)),
            gauge: gauge,
            combo: combo,
            maxCombo: maxCombo,
            lastCost: 0);

        Assert.Throws<InvalidOperationException>(() => BattleEngine.PlayCard(
            state, 0, 0, 0, Rng(), Catalog(def)));
    }

    [Fact]
    public void Combo_count_saturates_instead_of_overflowing()
    {
        var def = Card("c1");
        var state = State(
            ImmutableArray.Create(new BattleCardInstance("card-1", def.Id, false, null)),
            gauge: 2,
            combo: int.MaxValue,
            maxCombo: int.MaxValue,
            lastCost: 0);

        var (next, events) = BattleEngine.PlayCard(
            state, 0, 0, 0, Rng(), Catalog(def));

        Assert.Equal(int.MaxValue, next.ComboCount);
        Assert.Equal(int.MaxValue, next.MaxComboThisTurn);
        Assert.Equal(2, next.ComboGauge);
        Assert.DoesNotContain(events, e => e.Kind == BattleEventKind.ComboGaugeChanged);
    }

    [Fact]
    public void Choice_card_charges_gauge_before_pause_and_skills_are_locked()
    {
        var choose = ChooseCard();
        var filler = Card("filler", 0);
        var hand = ImmutableArray.Create(
            new BattleCardInstance("choose-1", choose.Id, false, null),
            new BattleCardInstance("filler-1", filler.Id, false, null),
            new BattleCardInstance("filler-2", filler.Id, false, null));
        var state = State(hand);

        var (paused, events) = BattleEngine.PlayCard(
            state, 0, 0, 0, Rng(), Catalog(choose, filler));

        Assert.NotNull(paused.PendingCardPlay);
        Assert.Equal(1, paused.ComboGauge);
        Assert.Contains(events, e =>
            e.Kind == BattleEventKind.ComboGaugeChanged && e.Amount == 1);

        var enoughGauge = paused with { ComboGauge = 10 };
        Assert.Throws<InvalidOperationException>(() =>
            BattleEngine.UseTacticalSkill(enoughGauge));
        Assert.Throws<InvalidOperationException>(() =>
            UseUltimate(enoughGauge));
    }

    [Fact]
    public void Tactical_skill_spends_three_and_applies_strength_and_omnistrike()
    {
        var state = State(ImmutableArray<BattleCardInstance>.Empty, gauge: 3);

        var (next, events) = BattleEngine.UseTacticalSkill(state);

        var hero = Assert.Single(next.Allies, a => a.DefinitionId == "hero");
        Assert.Equal(0, next.ComboGauge);
        Assert.Equal(1, hero.GetStatus("strength"));
        Assert.Equal(1, hero.GetStatus("omnistrike"));
        Assert.Equal(CombatSourceKind.CharacterSkill, hero.GetStatusSource("strength")!.Kind);
        Assert.Equal(CombatSourceKind.CharacterSkill, hero.GetStatusSource("omnistrike")!.Kind);
        Assert.True(next.UsedCharacterSkillIdsThisTurn.SetEquals(new[] { "tactical" }));

        Assert.Collection(events,
            e =>
            {
                Assert.Equal(BattleEventKind.UseCharacterSkill, e.Kind);
                Assert.Equal(3, e.Amount);
                Assert.Equal("tactical", e.Note);
            },
            e =>
            {
                Assert.Equal(BattleEventKind.ComboGaugeChanged, e.Kind);
                Assert.Equal(-3, e.Amount);
                Assert.Equal("remaining=0", e.Note);
            },
            e =>
            {
                Assert.Equal(BattleEventKind.ApplyStatus, e.Kind);
                Assert.Equal("strength", e.Note);
                Assert.Equal(CombatSourceKind.CharacterSkill, e.Source!.Kind);
            },
            e =>
            {
                Assert.Equal(BattleEventKind.ApplyStatus, e.Kind);
                Assert.Equal("omnistrike", e.Note);
                Assert.Equal(CombatSourceKind.CharacterSkill, e.Source!.Kind);
            });
    }

    [Fact]
    public void Tactical_skill_resonates_its_grouped_buffs_to_other_allies()
    {
        var hero = BattleFixtures.Hero() with
        {
            Statuses = ImmutableDictionary<string, int>.Empty
                .Add(BattleStatusIds.Resonance, 1),
        };
        var ally = BattleFixtures.SummonActor("ally-1", "ally", 1);
        var state = State(
            ImmutableArray<BattleCardInstance>.Empty,
            gauge: BattleEngine.TacticalSkillGaugeCost) with
        {
            Allies = [hero, ally],
        };

        var (next, events) = BattleEngine.UseTacticalSkill(state);

        Assert.Equal(1, next.Allies[0].GetStatus(BattleStatusIds.Strength));
        Assert.Equal(1, next.Allies[0].GetStatus(BattleStatusIds.Omnistrike));
        Assert.Equal(0, next.Allies[0].GetStatus(BattleStatusIds.Resonance));
        Assert.Equal(1, next.Allies[1].GetStatus(BattleStatusIds.Strength));
        Assert.Equal(1, next.Allies[1].GetStatus(BattleStatusIds.Omnistrike));
        Assert.Equal(2, events.Count(battleEvent =>
            battleEvent.Kind == BattleEventKind.ApplyStatus
            && battleEvent.TargetInstanceId == ally.InstanceId));
        Assert.Single(events, battleEvent =>
            battleEvent.Note == BattleStatusIds.Resonance
            && battleEvent.Amount == -1);
    }

    [Fact]
    public void Tactical_skill_rejects_second_use_in_same_turn()
    {
        var state = State(ImmutableArray<BattleCardInstance>.Empty, gauge: 6);

        var (afterFirst, _) = BattleEngine.UseTacticalSkill(state);

        Assert.Throws<InvalidOperationException>(() =>
            BattleEngine.UseTacticalSkill(afterFirst));
        Assert.Equal(3, afterFirst.ComboGauge);
        Assert.True(afterFirst.UsedCharacterSkillIdsThisTurn.SetEquals(
            new[] { "tactical" }));
    }

    [Fact]
    public void Tactical_skill_can_be_used_again_on_next_turn()
    {
        var state = State(ImmutableArray<BattleCardInstance>.Empty, gauge: 6);
        var (afterFirst, _) = BattleEngine.UseTacticalSkill(state);

        var (nextTurn, _) = BattleEngine.EndTurn(
            afterFirst, Rng(), BattleFixtures.MinimalCatalog());
        var (afterSecond, _) = BattleEngine.UseTacticalSkill(nextTurn);

        Assert.Equal(2, nextTurn.Turn);
        Assert.Empty(nextTurn.UsedCharacterSkillIdsThisTurn);
        Assert.Equal(0, afterSecond.ComboGauge);
        Assert.True(afterSecond.UsedCharacterSkillIdsThisTurn.SetEquals(
            new[] { "tactical" }));
    }

    [Fact]
    public void Ultimate_is_independent_of_tactical_once_per_turn_limit()
    {
        var hand = ImmutableArray.Create(
            new BattleCardInstance("hand-1", "any", false, null));
        var state = State(hand, gauge: 3);
        var (afterTactical, _) = BattleEngine.UseTacticalSkill(state);

        var (afterUltimate, _) = UseUltimate(
            afterTactical with { ComboGauge = BattleEngine.UltimateSkillGaugeCost });
        var (afterSecondUltimate, _) = UseUltimate(
            afterUltimate with { ComboGauge = BattleEngine.UltimateSkillGaugeCost });

        Assert.True(afterUltimate.UsedCharacterSkillIdsThisTurn.SetEquals(
            new[] { "tactical" }));
        Assert.True(afterSecondUltimate.UsedCharacterSkillIdsThisTurn.SetEquals(
            new[] { "tactical" }));
    }

    [Fact]
    public void Tactical_skill_saturates_strength_and_increments_existing_omnistrike()
    {
        var oldSource = CombatSource.Unattributed;
        var hero = BattleFixtures.Hero() with
        {
            Statuses = ImmutableDictionary<string, int>.Empty
                .Add("strength", int.MaxValue)
                .Add("omnistrike", 4),
            StatusSources = ImmutableDictionary<string, CombatSource>.Empty
                .Add("strength", oldSource)
                .Add("omnistrike", oldSource),
        };
        var state = State(ImmutableArray<BattleCardInstance>.Empty, gauge: 3) with
        {
            Allies = ImmutableArray.Create(hero),
        };

        var (next, events) = BattleEngine.UseTacticalSkill(state);

        Assert.Equal(int.MaxValue, next.Allies[0].GetStatus("strength"));
        Assert.Equal(5, next.Allies[0].GetStatus("omnistrike"));
        Assert.Same(oldSource, next.Allies[0].GetStatusSource("strength"));
        Assert.Equal(
            CombatSourceKind.CharacterSkill,
            next.Allies[0].GetStatusSource("omnistrike")!.Kind);
        var applied = Assert.Single(events, e => e.Kind == BattleEventKind.ApplyStatus);
        Assert.Equal("omnistrike", applied.Note);
        Assert.Equal(1, applied.Amount);
        Assert.Equal(CombatSourceKind.CharacterSkill, applied.Source!.Kind);
    }

    [Fact]
    public void Tactical_skill_saturates_omnistrike_at_int_max()
    {
        var oldSource = CombatSource.Unattributed;
        var hero = BattleFixtures.Hero() with
        {
            Statuses = ImmutableDictionary<string, int>.Empty
                .Add("omnistrike", int.MaxValue),
            StatusSources = ImmutableDictionary<string, CombatSource>.Empty
                .Add("omnistrike", oldSource),
        };
        var state = State(ImmutableArray<BattleCardInstance>.Empty, gauge: 3) with
        {
            Allies = ImmutableArray.Create(hero),
        };

        var (next, events) = BattleEngine.UseTacticalSkill(state);

        Assert.Equal(int.MaxValue, next.Allies[0].GetStatus("omnistrike"));
        Assert.Same(oldSource, next.Allies[0].GetStatusSource("omnistrike"));
        Assert.DoesNotContain(events, e =>
            e.Kind == BattleEventKind.ApplyStatus && e.Note == "omnistrike");
    }

    [Fact]
    public void Tactical_skill_uses_living_hero_with_lowest_slot()
    {
        var highSlot = BattleFixtures.Hero() with
        {
            InstanceId = "hero-high",
            SlotIndex = 5,
        };
        var lowSlot = BattleFixtures.Hero() with
        {
            InstanceId = "hero-low",
            SlotIndex = 1,
        };
        var state = State(ImmutableArray<BattleCardInstance>.Empty, gauge: 3) with
        {
            Allies = ImmutableArray.Create(highSlot, lowSlot),
        };

        var (next, _) = BattleEngine.UseTacticalSkill(state);

        Assert.Equal(0, next.Allies[0].GetStatus("strength"));
        Assert.Equal(1, next.Allies[1].GetStatus("strength"));
    }

    [Fact]
    public void Tactical_skill_omnistrike_makes_queued_single_hit_land_on_every_enemy()
    {
        var hero = BattleFixtures.Hero() with
        {
            AttackSingle = AttackPool.Empty.Add(5),
        };
        var state = State(ImmutableArray<BattleCardInstance>.Empty, gauge: 3) with
        {
            Allies = ImmutableArray.Create(hero),
            Enemies = ImmutableArray.Create(
                BattleFixtures.Goblin(0, hp: 20),
                BattleFixtures.Goblin(1, hp: 20)),
        };

        var (afterSkill, _) = BattleEngine.UseTacticalSkill(state);
        var (afterTurn, _) = BattleEngine.EndTurn(
            afterSkill, Rng(), BattleFixtures.MinimalCatalog());

        Assert.Equal(new[] { 14, 14 }, afterTurn.Enemies.Select(e => e.CurrentHp));
        Assert.Equal(0, afterTurn.Allies[0].GetStatus("omnistrike"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Character_skill_rejects_when_no_living_hero_exists(bool keepHeroDefinition)
    {
        var ally = BattleFixtures.Hero() with
        {
            DefinitionId = keepHeroDefinition ? "hero" : "summon",
            CurrentHp = keepHeroDefinition ? 0 : 10,
        };
        var state = State(
            ImmutableArray.Create(new BattleCardInstance("card", "any", false, null)),
            gauge: 10) with
        {
            Allies = ImmutableArray.Create(ally),
        };

        Assert.Throws<InvalidOperationException>(() =>
            BattleEngine.UseTacticalSkill(state));
        Assert.Throws<InvalidOperationException>(() =>
            UseUltimate(state));
    }

    [Fact]
    public void Ultimate_spends_six_and_snapshots_only_current_hand_instances()
    {
        var c1 = new BattleCardInstance("hand-1", "expensive", false, null);
        var c2 = new BattleCardInstance("hand-2", "expensive", false, null);
        var draw = new BattleCardInstance("draw-1", "expensive", false, null);
        var state = State(ImmutableArray.Create(c1, c2), gauge: 6) with
        {
            DrawPile = ImmutableArray.Create(draw),
        };

        var (next, events) = UseUltimate(state);

        Assert.Equal(0, next.ComboGauge);
        Assert.All(next.Hand, card =>
        {
            var modifier = Assert.Single(card.EffectiveCostModifiers);
            Assert.Equal(-1, modifier.Amount);
            Assert.Equal(CardCostModifierDuration.UntilTurnEnd, modifier.Duration);
            Assert.Equal("ultimate", modifier.SourceId);
        });
        Assert.Empty(Assert.Single(next.DrawPile).EffectiveCostModifiers);
        Assert.Collection(events,
            e =>
            {
                Assert.Equal(BattleEventKind.UseCharacterSkill, e.Kind);
                Assert.Equal(6, e.Amount);
                Assert.Equal("ultimate", e.Note);
            },
            e =>
            {
                Assert.Equal(BattleEventKind.ComboGaugeChanged, e.Kind);
                Assert.Equal(-6, e.Amount);
                Assert.Equal("remaining=0", e.Note);
            },
            e =>
            {
                Assert.Equal(BattleEventKind.ModifyCardCost, e.Kind);
                Assert.Equal("hand-1", e.TargetInstanceId);
                Assert.Equal(-1, e.Amount);
            },
            e =>
            {
                Assert.Equal(BattleEventKind.ModifyCardCost, e.Kind);
                Assert.Equal("hand-2", e.TargetInstanceId);
                Assert.Equal(-1, e.Amount);
            });
    }

    [Fact]
    public void Ultimate_stacks_snapshot_reduction_with_existing_card_modifier()
    {
        var existing = new CardCostModifier(
            +1, CardCostModifierDuration.UntilPlayed, "earlier");
        var state = State(
            ImmutableArray.Create(
                new BattleCardInstance("current", "any", false, null,
                    ImmutableArray.Create(existing))),
            gauge: 10);

        var (next, _) = UseUltimate(state);

        Assert.Collection(next.Hand[0].EffectiveCostModifiers,
            modifier => Assert.Equal(existing, modifier),
            modifier =>
            {
                Assert.Equal(-2, modifier.Amount);
                Assert.Equal(CardCostModifierDuration.UntilTurnEnd, modifier.Duration);
            });
    }

    [Fact]
    public void Ultimate_immediately_zeroes_even_a_precombo_cost_above_int_max()
    {
        var definition = Card("overflow-cost", 1);
        var hero = BattleFixtures.Hero() with
        {
            Statuses = BattleFixtures.Hero().Statuses
                .SetItem(BattleCardCostCalculator.HandCostIncreaseStatusId, int.MaxValue),
        };
        var state = State(
            ImmutableArray.Create(new BattleCardInstance(
                "overflow", definition.Id, false, null)),
            gauge: 10) with
        {
            Allies = ImmutableArray.Create(hero),
        };

        var (next, _) = UseUltimate(state, Catalog(definition));
        var modifier = Assert.Single(next.Hand[0].EffectiveCostModifiers);
        var current = BattleCardCostCalculator.EvaluateForHand(
            next, next.Hand[0], definition, next.Allies[0]);

        Assert.Equal(-((long)int.MaxValue + 1), modifier.Amount);
        Assert.Equal(0, current.PayableCost);
    }

    [Fact]
    public void Ultimate_zero_set_has_priority_during_payment_but_not_for_later_cards()
    {
        var expensive = Card("expensive", 5);
        var marked = new BattleCardInstance("marked", expensive.Id, false, CostOverride: 4);
        var later = new BattleCardInstance("later", expensive.Id, false, null);
        var state = State(ImmutableArray.Create(marked), gauge: 10, energy: 0);
        var catalog = Catalog(expensive);

        var (afterUltimate, _) = UseUltimate(state, catalog);
        var (afterMarked, events) = BattleEngine.PlayCard(
            afterUltimate, 0, 0, 0, Rng(), catalog);

        Assert.Equal(0, afterMarked.Energy);
        Assert.Equal(0, Assert.Single(events, e => e.Kind == BattleEventKind.PlayCard).Amount);
        Assert.Empty(Assert.Single(afterMarked.DiscardPile).EffectiveCostModifiers);

        var withLaterCard = afterMarked with { Hand = ImmutableArray.Create(later) };
        Assert.Throws<InvalidOperationException>(() =>
            BattleEngine.PlayCard(withLaterCard, 0, 0, 0, Rng(), catalog));
    }

    [Theory]
    [InlineData("tactical", 2)]
    [InlineData("ultimate", 5)]
    public void Character_skill_rejects_insufficient_gauge(string skill, int gauge)
    {
        var state = State(
            ImmutableArray.Create(new BattleCardInstance("card", "any", false, null)),
            gauge: gauge);

        Assert.Throws<InvalidOperationException>(() => Use(skill, state));
    }

    [Theory]
    [InlineData("tactical")]
    [InlineData("ultimate")]
    public void Character_skill_rejects_enemy_phase(string skill)
    {
        var state = State(
            ImmutableArray.Create(new BattleCardInstance("card", "any", false, null)),
            gauge: 10) with { Phase = BattlePhase.EnemyAttacking };

        Assert.Throws<InvalidOperationException>(() => Use(skill, state));
    }

    [Theory]
    [InlineData("tactical")]
    [InlineData("ultimate")]
    public void Character_skill_rejects_resolved_battle(string skill)
    {
        var state = State(
            ImmutableArray.Create(new BattleCardInstance("card", "any", false, null)),
            gauge: 10) with
        {
            Phase = BattlePhase.Resolved,
            Outcome = BattleOutcome.Victory,
        };

        Assert.Throws<InvalidOperationException>(() => Use(skill, state));
    }

    [Fact]
    public void Ultimate_allows_empty_hand_as_an_empty_effect_resolution()
    {
        var state = State(ImmutableArray<BattleCardInstance>.Empty, gauge: 6);

        var (next, events) = UseUltimate(state);
        Assert.Equal(0, next.ComboGauge);
        Assert.Contains(events, e => e.Kind == BattleEventKind.UseCharacterSkill);
    }

    [Theory]
    [InlineData("tactical")]
    [InlineData("ultimate")]
    public void Character_skill_rejects_gauge_above_state_limit(string skill)
    {
        var state = State(
            ImmutableArray.Create(new BattleCardInstance("card", "any", false, null)),
            gauge: 11);

        Assert.Throws<InvalidOperationException>(() => Use(skill, state));
    }

    [Fact]
    public void Turn_processors_clear_turn_scoped_modifiers_and_skill_usage_but_keep_gauge()
    {
        var marked = new BattleCardInstance("marked", "any", false, null)
            .AddCostModifier(new CardCostModifier(
                -1, CardCostModifierDuration.UntilTurnEnd, "ultimate"));
        var state = State(ImmutableArray.Create(marked), gauge: 7) with
        {
            UsedCharacterSkillIdsThisTurn =
                ImmutableHashSet.Create(StringComparer.Ordinal, "tactical"),
            CardUsesThisTurn = ImmutableDictionary<string, int>.Empty.Add("card", 1),
            PotionUsesThisTurn = ImmutableDictionary<string, int>.Empty.Add("potion", 1),
        };

        var (afterEnd, _) = TurnEndProcessor.Process(
            state, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(7, afterEnd.ComboGauge);
        Assert.All(AllCards(afterEnd), card => Assert.Empty(card.EffectiveCostModifiers));
        Assert.Empty(afterEnd.UsedCharacterSkillIdsThisTurn);
        Assert.Empty(afterEnd.CardUsesThisTurn);
        Assert.Empty(afterEnd.PotionUsesThisTurn);

        var directStart = state with { Turn = 2 };
        var (afterStart, _) = TurnStartProcessor.Process(
            directStart, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(7, afterStart.ComboGauge);
        Assert.All(AllCards(afterStart), card => Assert.Empty(card.EffectiveCostModifiers));
        Assert.Empty(afterStart.UsedCharacterSkillIdsThisTurn);
        Assert.Empty(afterStart.CardUsesThisTurn);
        Assert.Empty(afterStart.PotionUsesThisTurn);

        var (afterNormalTurn, _) = BattleEngine.EndTurn(
            state, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(BattleOutcome.Pending, afterNormalTurn.Outcome);
        Assert.Equal(7, afterNormalTurn.ComboGauge);
        Assert.All(AllCards(afterNormalTurn), card => Assert.Empty(card.EffectiveCostModifiers));
        Assert.Empty(afterNormalTurn.UsedCharacterSkillIdsThisTurn);
        Assert.Empty(afterNormalTurn.CardUsesThisTurn);
        Assert.Empty(afterNormalTurn.PotionUsesThisTurn);
    }

    [Fact]
    public void Victory_early_return_clears_turn_cost_modifiers_and_keeps_gauge()
    {
        var hero = BattleFixtures.Hero() with
        {
            AttackSingle = AttackPool.Empty.Add(99),
        };
        var marked = new BattleCardInstance("marked", "any", false, null)
            .AddCostModifier(new CardCostModifier(
                -1, CardCostModifierDuration.UntilTurnEnd));
        var state = State(ImmutableArray.Create(marked), gauge: 7) with
        {
            Allies = ImmutableArray.Create(hero),
            Enemies = ImmutableArray.Create(BattleFixtures.Goblin(hp: 1)),
            UsedCharacterSkillIdsThisTurn = ImmutableHashSet.Create("tactical"),
        };

        var (next, _) = BattleEngine.EndTurn(
            state, Rng(), BattleFixtures.MinimalCatalog());

        Assert.Equal(BattleOutcome.Victory, next.Outcome);
        Assert.Equal(7, next.ComboGauge);
        Assert.All(AllCards(next), card => Assert.Empty(card.EffectiveCostModifiers));
        Assert.Empty(next.UsedCharacterSkillIdsThisTurn);
    }

    [Fact]
    public void Defeat_early_return_clears_turn_cost_modifiers_and_keeps_gauge()
    {
        var marked = new BattleCardInstance("marked", "any", false, null)
            .AddCostModifier(new CardCostModifier(
                -1, CardCostModifierDuration.UntilTurnEnd));
        var state = State(ImmutableArray.Create(marked), gauge: 7) with
        {
            Allies = ImmutableArray.Create(BattleFixtures.Hero(hp: 1)),
            UsedCharacterSkillIdsThisTurn = ImmutableHashSet.Create("tactical"),
        };
        var catalog = BattleFixtures.MinimalCatalog(
            enemies: new[] { BattleFixtures.GoblinDef(hp: 20, attack: 5) });

        var (next, _) = BattleEngine.EndTurn(state, Rng(), catalog);

        Assert.Equal(BattleOutcome.Defeat, next.Outcome);
        Assert.Equal(7, next.ComboGauge);
        Assert.All(AllCards(next), card => Assert.Empty(card.EffectiveCostModifiers));
        Assert.Empty(next.UsedCharacterSkillIdsThisTurn);
    }

    [Fact]
    public void Used_character_skill_ids_default_empty_and_affect_record_equality()
    {
        var state = State(ImmutableArray<BattleCardInstance>.Empty);

        Assert.Empty(state.UsedCharacterSkillIdsThisTurn);

        var used = state with
        {
            UsedCharacterSkillIdsThisTurn =
                state.UsedCharacterSkillIdsThisTurn.Add("tactical"),
        };
        Assert.NotEqual(state, used);

        var reset = used with
        {
            UsedCharacterSkillIdsThisTurn = state.UsedCharacterSkillIdsThisTurn,
        };
        Assert.Equal(state, reset);
    }

    [Fact]
    public void Character_skill_results_and_events_are_deterministic()
    {
        var hand = ImmutableArray.Create(
            new BattleCardInstance("a", "any", false, null),
            new BattleCardInstance("b", "any", false, null));
        var state = State(hand, gauge: 10);

        var (a, eventsA) = UseUltimate(state);
        var (b, eventsB) = UseUltimate(state);

        Assert.Equal(a.ComboGauge, b.ComboGauge);
        Assert.True(a.Hand.SequenceEqual(b.Hand));
        Assert.True(a.UsedCharacterSkillIdsThisTurn.SetEquals(
            b.UsedCharacterSkillIdsThisTurn));
        Assert.Equal(eventsA, eventsB);

        var tacticalState = State(
            ImmutableArray<BattleCardInstance>.Empty,
            gauge: BattleEngine.TacticalSkillGaugeCost);
        var (tacticalA, tacticalEventsA) =
            BattleEngine.UseTacticalSkill(tacticalState);
        var (tacticalB, tacticalEventsB) =
            BattleEngine.UseTacticalSkill(tacticalState);

        Assert.True(tacticalA.UsedCharacterSkillIdsThisTurn.SetEquals(
            tacticalB.UsedCharacterSkillIdsThisTurn));
        Assert.Equal(tacticalEventsA, tacticalEventsB);
    }

    private static (BattleState, IReadOnlyList<BattleEvent>) Use(
        string skill, BattleState state) =>
        skill == "tactical"
            ? BattleEngine.UseTacticalSkill(state)
            : UseUltimate(state);
}
