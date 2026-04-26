using System;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class BattleEngineUsePotionTests
{
    private static FakeRng MakeRng() => new FakeRng(new int[20], System.Array.Empty<double>());

    [Fact]
    public void UsePotion_throws_when_phase_not_PlayerInput()
    {
        var potion = BattleFixtures.Potion("p1",
            new CardEffect("heal", EffectScope.Self, null, 5));
        var catalog = BattleFixtures.MinimalCatalog(potions: new[] { potion });
        var state = BattleFixtures.MinimalState(
            potions: ImmutableArray.Create("p1", "", "")) with { Phase = BattlePhase.PlayerAttacking };

        Assert.Throws<InvalidOperationException>(() =>
            BattleEngine.UsePotion(state, 0, null, null, MakeRng(), catalog));
    }

    [Fact]
    public void UsePotion_throws_when_potionIndex_out_of_range()
    {
        var catalog = BattleFixtures.MinimalCatalog();
        var state = BattleFixtures.MinimalState(
            potions: ImmutableArray.Create("", "", ""));

        Assert.Throws<InvalidOperationException>(() =>
            BattleEngine.UsePotion(state, 5, null, null, MakeRng(), catalog));
    }

    [Fact]
    public void UsePotion_throws_when_slot_empty()
    {
        var catalog = BattleFixtures.MinimalCatalog();
        var state = BattleFixtures.MinimalState(
            potions: ImmutableArray.Create("", "", ""));

        Assert.Throws<InvalidOperationException>(() =>
            BattleEngine.UsePotion(state, 0, null, null, MakeRng(), catalog));
    }

    [Fact]
    public void UsePotion_throws_when_potion_not_in_catalog()
    {
        var catalog = BattleFixtures.MinimalCatalog();
        var state = BattleFixtures.MinimalState(
            potions: ImmutableArray.Create("missing", "", ""));

        Assert.Throws<InvalidOperationException>(() =>
            BattleEngine.UsePotion(state, 0, null, null, MakeRng(), catalog));
    }

    [Fact]
    public void UsePotion_applies_heal_effect_and_consumes_slot()
    {
        var potion = BattleFixtures.Potion("heal_p",
            new CardEffect("heal", EffectScope.Self, null, 10));
        var catalog = BattleFixtures.MinimalCatalog(potions: new[] { potion });
        var injuredHero = BattleFixtures.Hero(hp: 70) with { CurrentHp = 30 };
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(injuredHero),
            potions: ImmutableArray.Create("heal_p", "", ""));

        var (after, events) = BattleEngine.UsePotion(state, 0, null, null, MakeRng(), catalog);

        Assert.Equal(40, after.Allies[0].CurrentHp);
        Assert.Equal("", after.Potions[0]);
        Assert.Contains(events, e => e.Kind == BattleEventKind.UsePotion);
    }

    [Fact]
    public void UsePotion_applies_attack_effect_to_hero_pool()
    {
        var potion = BattleFixtures.Potion("atk_p",
            new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 3));
        var catalog = BattleFixtures.MinimalCatalog(potions: new[] { potion });
        var state = BattleFixtures.MinimalState(
            potions: ImmutableArray.Create("atk_p", "", ""));

        var (after, _) = BattleEngine.UsePotion(state, 0, null, null, MakeRng(), catalog);

        Assert.Equal(3, after.Allies[0].AttackAll.Sum);
    }

    [Fact]
    public void UsePotion_does_not_consume_energy()
    {
        var potion = BattleFixtures.Potion("p",
            new CardEffect("heal", EffectScope.Self, null, 1));
        var catalog = BattleFixtures.MinimalCatalog(potions: new[] { potion });
        var state = BattleFixtures.MinimalState(
            potions: ImmutableArray.Create("p", "", "")) with { Energy = 2 };

        var (after, _) = BattleEngine.UsePotion(state, 0, null, null, MakeRng(), catalog);

        Assert.Equal(2, after.Energy);
    }

    [Fact]
    public void UsePotion_does_not_update_combo_fields()
    {
        var potion = BattleFixtures.Potion("p",
            new CardEffect("heal", EffectScope.Self, null, 1));
        var catalog = BattleFixtures.MinimalCatalog(potions: new[] { potion });
        var state = BattleFixtures.MinimalState(
            potions: ImmutableArray.Create("p", "", "")) with {
            ComboCount = 5,
            LastPlayedOrigCost = 2,
            NextCardComboFreePass = true,
        };

        var (after, _) = BattleEngine.UsePotion(state, 0, null, null, MakeRng(), catalog);

        Assert.Equal(5, after.ComboCount);
        Assert.Equal(2, after.LastPlayedOrigCost);
        Assert.True(after.NextCardComboFreePass);
    }

    [Fact]
    public void UsePotion_updates_target_when_arg_provided()
    {
        var potion = BattleFixtures.Potion("p",
            new CardEffect("heal", EffectScope.Self, null, 1));
        var catalog = BattleFixtures.MinimalCatalog(potions: new[] { potion });
        var state = BattleFixtures.MinimalState(
            enemies: ImmutableArray.Create(
                BattleFixtures.Goblin(slotIndex: 0),
                BattleFixtures.Goblin(slotIndex: 1)),
            potions: ImmutableArray.Create("p", "", ""));

        var (after, _) = BattleEngine.UsePotion(state, 0, targetEnemyIndex: 1, null, MakeRng(), catalog);

        Assert.Equal(1, after.TargetEnemyIndex);
    }

    [Fact]
    public void UsePotion_keeps_existing_target_when_arg_null()
    {
        var potion = BattleFixtures.Potion("p",
            new CardEffect("heal", EffectScope.Self, null, 1));
        var catalog = BattleFixtures.MinimalCatalog(potions: new[] { potion });
        var state = BattleFixtures.MinimalState(
            potions: ImmutableArray.Create("p", "", "")) with { TargetEnemyIndex = 0 };

        var (after, _) = BattleEngine.UsePotion(state, 0, null, null, MakeRng(), catalog);

        Assert.Equal(0, after.TargetEnemyIndex);
    }

    [Fact]
    public void UsePotion_event_fires_with_potion_id_and_slot_index()
    {
        var potion = BattleFixtures.Potion("p",
            new CardEffect("heal", EffectScope.Self, null, 1));
        var catalog = BattleFixtures.MinimalCatalog(potions: new[] { potion });
        var state = BattleFixtures.MinimalState(
            potions: ImmutableArray.Create("", "p", ""));

        var (_, events) = BattleEngine.UsePotion(state, 1, null, null, MakeRng(), catalog);

        var ev = events.First(e => e.Kind == BattleEventKind.UsePotion);
        Assert.Equal("p", ev.CardId);
        Assert.Equal(1, ev.Amount);
    }

    [Fact]
    public void UsePotion_consecutive_in_same_turn_works()
    {
        var p1 = BattleFixtures.Potion("p1",
            new CardEffect("heal", EffectScope.Self, null, 5));
        var p2 = BattleFixtures.Potion("p2",
            new CardEffect("heal", EffectScope.Self, null, 7));
        var catalog = BattleFixtures.MinimalCatalog(potions: new[] { p1, p2 });
        var injuredHero = BattleFixtures.Hero() with { CurrentHp = 30 };
        var state = BattleFixtures.MinimalState(
            allies: ImmutableArray.Create(injuredHero),
            potions: ImmutableArray.Create("p1", "p2", ""));

        var (after1, _) = BattleEngine.UsePotion(state, 0, null, null, MakeRng(), catalog);
        var (after2, _) = BattleEngine.UsePotion(after1, 1, null, null, MakeRng(), catalog);

        Assert.Equal(42, after2.Allies[0].CurrentHp);
        Assert.Equal("", after2.Potions[0]);
        Assert.Equal("", after2.Potions[1]);
    }
}
