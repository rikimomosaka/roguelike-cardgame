using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class EffectApplierHealTests
{
    private static IRng Rng() => new FakeRng(new int[10], new double[0]);

    private static BattleState MakeState(CombatActor hero, params CombatActor[] otherAllies)
    {
        var allies = ImmutableArray.Create<CombatActor>(hero).AddRange(otherAllies);
        return new BattleState(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: allies,
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: 3, EnergyMax: 3,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0, LastPlayedOrigCost: null, NextCardComboFreePass: false,
            OwnedRelicIds: ImmutableArray<string>.Empty,
            Potions: ImmutableArray<string>.Empty,
            EncounterId: "enc_test");
    }

    [Fact] public void Heal_self_increases_caster_hp()
    {
        var hero = BattleFixtures.Hero(hp: 70) with { CurrentHp = 50 };
        var s = MakeState(hero);
        var eff = new CardEffect("heal", EffectScope.Self, null, 10);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(60, next.Allies[0].CurrentHp);
        Assert.Single(evs);
        Assert.Equal(BattleEventKind.Heal, evs[0].Kind);
        Assert.Equal(10, evs[0].Amount);
    }

    [Fact] public void Heal_caps_at_max_hp()
    {
        var hero = BattleFixtures.Hero(hp: 70) with { CurrentHp = 65 };
        var s = MakeState(hero);
        var eff = new CardEffect("heal", EffectScope.Self, null, 10);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(70, next.Allies[0].CurrentHp);
        Assert.Equal(5, evs[0].Amount); // 実回復量 = min(10, 70-65) = 5
    }

    [Fact] public void Heal_at_max_hp_emits_no_event()
    {
        var hero = BattleFixtures.Hero(hp: 70);  // CurrentHp == MaxHp == 70
        var s = MakeState(hero);
        var eff = new CardEffect("heal", EffectScope.Self, null, 10);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(70, next.Allies[0].CurrentHp);
        Assert.Empty(evs);
    }

    [Fact] public void Heal_single_ally_uses_target_index()
    {
        var hero = BattleFixtures.Hero(hp: 70) with { CurrentHp = 30 };
        var summon = BattleFixtures.SummonActor("s1", "minion", slotIndex: 1, hp: 20)
            with { CurrentHp = 5 };
        var s = MakeState(hero, summon) with { TargetAllyIndex = 1 };
        var eff = new CardEffect("heal", EffectScope.Single, EffectSide.Ally, 10);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(30, next.Allies[0].CurrentHp);  // hero unchanged
        Assert.Equal(15, next.Allies[1].CurrentHp);  // summon healed 5→15
    }

    [Fact] public void Heal_all_ally_heals_living_allies()
    {
        var hero = BattleFixtures.Hero(hp: 70) with { CurrentHp = 30 };
        var summon1 = BattleFixtures.SummonActor("s1", "minion", slotIndex: 1, hp: 20)
            with { CurrentHp = 5 };
        var summon2 = BattleFixtures.SummonActor("s2", "minion", slotIndex: 2, hp: 20)
            with { CurrentHp = 0 };  // dead
        var s = MakeState(hero, summon1, summon2);
        var eff = new CardEffect("heal", EffectScope.All, EffectSide.Ally, 10);
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(40, next.Allies[0].CurrentHp);
        Assert.Equal(15, next.Allies[1].CurrentHp);
        Assert.Equal(0, next.Allies[2].CurrentHp);  // dead skip
        Assert.Equal(2, evs.Count);  // 2 living allies healed
    }

    [Fact] public void Heal_random_ally_picks_via_rng()
    {
        var hero = BattleFixtures.Hero(hp: 70) with { CurrentHp = 30 };
        var summon = BattleFixtures.SummonActor("s1", "minion", slotIndex: 1, hp: 20)
            with { CurrentHp = 5 };
        var s = MakeState(hero, summon);
        var eff = new CardEffect("heal", EffectScope.Random, EffectSide.Ally, 10);
        var rng = new FakeRng(new[] { 0 }, new double[0]);  // pick index 0 = hero
        var (next, evs) = EffectApplier.Apply(s, hero, eff, rng, BattleFixtures.MinimalCatalog());
        Assert.Equal(40, next.Allies[0].CurrentHp);
        Assert.Equal(5, next.Allies[1].CurrentHp);
    }
}
