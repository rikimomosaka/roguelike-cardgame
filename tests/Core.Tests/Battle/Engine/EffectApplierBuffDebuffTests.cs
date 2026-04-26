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

public class EffectApplierBuffDebuffTests
{
    private static BattleState State(CombatActor hero, params CombatActor[] enemies) => new(
        Turn: 1, Phase: BattlePhase.PlayerInput,
        Outcome: RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending,
        Allies: ImmutableArray.Create(hero),
        Enemies: enemies.ToImmutableArray(),
        TargetAllyIndex: 0, TargetEnemyIndex: 0,
        Energy: 3, EnergyMax: 3,
        DrawPile: ImmutableArray<BattleCardInstance>.Empty,
        Hand: ImmutableArray<BattleCardInstance>.Empty,
        DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
        ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
        ComboCount: 0,
        LastPlayedOrigCost: null,
        NextCardComboFreePass: false,
        EncounterId: "enc_test");

    private static IRng Rng(params int[] ints) => new FakeRng(ints, new double[0]);

    [Fact] public void Buff_self_adds_strength_to_caster()
    {
        var hero = BattleFixtures.Hero();
        var s = State(hero, BattleFixtures.Goblin());
        var eff = new CardEffect("buff", EffectScope.Self, null, 2, Name: "strength");
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng());
        Assert.Equal(2, next.Allies[0].GetStatus("strength"));
        Assert.Single(evs);
        Assert.Equal(BattleEventKind.ApplyStatus, evs[0].Kind);
        Assert.Equal("strength", evs[0].Note);
        Assert.Equal(2, evs[0].Amount);
    }

    [Fact] public void Debuff_single_enemy_adds_vulnerable_to_target()
    {
        var hero = BattleFixtures.Hero();
        var goblin = BattleFixtures.Goblin();
        var s = State(hero, goblin);
        var eff = new CardEffect("debuff", EffectScope.Single, EffectSide.Enemy, 1, Name: "vulnerable");
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng());
        Assert.Equal(1, next.Enemies[0].GetStatus("vulnerable"));
    }

    [Fact] public void Debuff_all_enemies_adds_weak_to_each()
    {
        var hero = BattleFixtures.Hero();
        var s = State(hero, BattleFixtures.Goblin(0), BattleFixtures.Goblin(1));
        var eff = new CardEffect("debuff", EffectScope.All, EffectSide.Enemy, 1, Name: "weak");
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng());
        Assert.Equal(1, next.Enemies[0].GetStatus("weak"));
        Assert.Equal(1, next.Enemies[1].GetStatus("weak"));
        Assert.Equal(2, evs.Count(e => e.Kind == BattleEventKind.ApplyStatus));
    }

    [Fact] public void Debuff_random_enemy_uses_rng()
    {
        var hero = BattleFixtures.Hero();
        var s = State(hero, BattleFixtures.Goblin(0), BattleFixtures.Goblin(1));
        var eff = new CardEffect("debuff", EffectScope.Random, EffectSide.Enemy, 1, Name: "weak");
        // FakeRng で index 1 を指す
        var (next, _) = EffectApplier.Apply(s, hero, eff, Rng(1));
        Assert.Equal(0, next.Enemies[0].GetStatus("weak"));
        Assert.Equal(1, next.Enemies[1].GetStatus("weak"));
    }

    [Fact] public void Buff_stacks_amount()
    {
        var hero = BattleFixtures.WithStrength(BattleFixtures.Hero(), 2);
        var s = State(hero, BattleFixtures.Goblin());
        var eff = new CardEffect("buff", EffectScope.Self, null, 3, Name: "strength");
        var (next, _) = EffectApplier.Apply(s, hero, eff, Rng());
        Assert.Equal(5, next.Allies[0].GetStatus("strength"));
    }

    [Fact] public void Negative_delta_below_zero_removes_status_and_emits_RemoveStatus()
    {
        var hero = BattleFixtures.WithStrength(BattleFixtures.Hero(), 2);
        var s = State(hero, BattleFixtures.Goblin());
        var eff = new CardEffect("debuff", EffectScope.Self, null, -5, Name: "strength");
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng());
        Assert.False(next.Allies[0].Statuses.ContainsKey("strength"));
        Assert.Equal(BattleEventKind.RemoveStatus, evs[0].Kind);
        Assert.Equal("strength", evs[0].Note);
    }

    [Fact] public void Buff_single_with_null_side_throws()
    {
        var hero = BattleFixtures.Hero();
        var s = State(hero, BattleFixtures.Goblin());
        var eff = new CardEffect("buff", EffectScope.Single, null, 1, Name: "strength");
        Assert.Throws<System.InvalidOperationException>(() => EffectApplier.Apply(s, hero, eff, Rng()));
    }

    [Fact] public void Debuff_single_with_no_target_index_is_noop()
    {
        var hero = BattleFixtures.Hero();
        var goblin = BattleFixtures.Goblin();
        var s = State(hero, goblin) with { TargetEnemyIndex = null };
        var eff = new CardEffect("debuff", EffectScope.Single, EffectSide.Enemy, 1, Name: "weak");
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng());
        Assert.Equal(0, next.Enemies[0].GetStatus("weak"));
        Assert.Empty(evs);
    }

    [Fact] public void Buff_random_ally_uses_rng()
    {
        var hero = BattleFixtures.Hero();
        var s = State(hero, BattleFixtures.Goblin());
        var eff = new CardEffect("buff", EffectScope.Random, EffectSide.Ally, 1, Name: "strength");
        var (next, _) = EffectApplier.Apply(s, hero, eff, Rng(0));
        Assert.Equal(1, next.Allies[0].GetStatus("strength"));
    }

    [Fact] public void ApplyStatus_event_caster_is_effect_caster()
    {
        var hero = BattleFixtures.Hero();
        var goblin = BattleFixtures.Goblin();
        var s = State(hero, goblin);
        var eff = new CardEffect("debuff", EffectScope.Single, EffectSide.Enemy, 1, Name: "vulnerable");
        var (_, evs) = EffectApplier.Apply(s, hero, eff, Rng());
        Assert.Equal(hero.InstanceId, evs[0].CasterInstanceId);
        Assert.Equal(goblin.InstanceId, evs[0].TargetInstanceId);
    }

    [Fact] public void Buff_random_with_null_side_throws()
    {
        var hero = BattleFixtures.Hero();
        var s = State(hero, BattleFixtures.Goblin());
        var eff = new CardEffect("buff", EffectScope.Random, null, 1, Name: "strength");
        Assert.Throws<System.InvalidOperationException>(() => EffectApplier.Apply(s, hero, eff, Rng()));
    }

    [Fact] public void Buff_all_with_null_side_throws()
    {
        var hero = BattleFixtures.Hero();
        var s = State(hero, BattleFixtures.Goblin());
        var eff = new CardEffect("buff", EffectScope.All, null, 1, Name: "strength");
        Assert.Throws<System.InvalidOperationException>(() => EffectApplier.Apply(s, hero, eff, Rng()));
    }

    [Fact] public void Buff_single_ally_targets_target_ally_index()
    {
        var hero = BattleFixtures.Hero();
        var s = State(hero, BattleFixtures.Goblin());
        var eff = new CardEffect("buff", EffectScope.Single, EffectSide.Ally, 2, Name: "strength");
        var (next, _) = EffectApplier.Apply(s, hero, eff, Rng());
        Assert.Equal(2, next.Allies[0].GetStatus("strength"));
    }

    [Fact] public void Buff_all_allies_adds_to_each()
    {
        var hero = BattleFixtures.Hero();
        var s = State(hero, BattleFixtures.Goblin());
        var eff = new CardEffect("buff", EffectScope.All, EffectSide.Ally, 1, Name: "strength");
        var (next, _) = EffectApplier.Apply(s, hero, eff, Rng());
        Assert.Equal(1, next.Allies[0].GetStatus("strength"));
    }
}
