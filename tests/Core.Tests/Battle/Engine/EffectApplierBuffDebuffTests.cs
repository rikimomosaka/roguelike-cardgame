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
        SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
        PowerCards: ImmutableArray<BattleCardInstance>.Empty,
        ComboCount: 0,
        LastPlayedOrigCost: null,
        NextCardComboFreePass: false,
        OwnedRelicIds: ImmutableArray<string>.Empty,
        Potions: ImmutableArray<string>.Empty,
        EncounterId: "enc_test");

    private static IRng Rng(params int[] ints) => new FakeRng(ints, new double[0]);

    [Fact] public void Buff_self_adds_strength_to_caster()
    {
        var hero = BattleFixtures.Hero();
        var s = State(hero, BattleFixtures.Goblin());
        var eff = new CardEffect("buff", EffectScope.Self, null, 2, Name: "strength");
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
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
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(1, next.Enemies[0].GetStatus("vulnerable"));
    }

    [Fact] public void Debuff_all_enemies_adds_weak_to_each()
    {
        var hero = BattleFixtures.Hero();
        var s = State(hero, BattleFixtures.Goblin(0), BattleFixtures.Goblin(1));
        var eff = new CardEffect("debuff", EffectScope.All, EffectSide.Enemy, 1, Name: "weak");
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
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
        var (next, _) = EffectApplier.Apply(s, hero, eff, Rng(1), BattleFixtures.MinimalCatalog());
        Assert.Equal(0, next.Enemies[0].GetStatus("weak"));
        Assert.Equal(1, next.Enemies[1].GetStatus("weak"));
    }

    [Fact] public void Buff_stacks_amount()
    {
        var hero = BattleFixtures.WithStrength(BattleFixtures.Hero(), 2);
        var s = State(hero, BattleFixtures.Goblin());
        var eff = new CardEffect("buff", EffectScope.Self, null, 3, Name: "strength");
        var (next, _) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(5, next.Allies[0].GetStatus("strength"));
    }

    [Fact] public void Negative_delta_on_strength_keeps_signed_value()
    {
        // Phase 10.6.B フォローアップ: strength は signed なので合計が負でも保持される。
        // (旧仕様: 0 以下で remove だったが、Heart 系のデバフを表現するため signed 化)
        var hero = BattleFixtures.WithStrength(BattleFixtures.Hero(), 2);
        var s = State(hero, BattleFixtures.Goblin());
        var eff = new CardEffect("debuff", EffectScope.Self, null, -5, Name: "strength");
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(-3, next.Allies[0].GetStatus("strength"));
        Assert.Single(evs);
        Assert.Equal(BattleEventKind.ApplyStatus, evs[0].Kind);
    }

    [Fact] public void Negative_delta_on_weak_below_zero_removes_status()
    {
        // weak は signed ではないので 0 以下で remove (従来通り)
        var hero = BattleFixtures.Hero() with {
            Statuses = ImmutableDictionary<string, int>.Empty.Add("weak", 2)
        };
        var s = State(hero, BattleFixtures.Goblin());
        var eff = new CardEffect("buff", EffectScope.Self, null, -5, Name: "weak");
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.False(next.Allies[0].Statuses.ContainsKey("weak"));
        Assert.Equal(BattleEventKind.RemoveStatus, evs[0].Kind);
    }

    [Fact] public void Buff_single_with_null_side_throws()
    {
        var hero = BattleFixtures.Hero();
        var s = State(hero, BattleFixtures.Goblin());
        var eff = new CardEffect("buff", EffectScope.Single, null, 1, Name: "strength");
        Assert.Throws<System.InvalidOperationException>(() => EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog()));
    }

    [Fact] public void Debuff_single_with_no_target_index_is_noop()
    {
        var hero = BattleFixtures.Hero();
        var goblin = BattleFixtures.Goblin();
        var s = State(hero, goblin) with { TargetEnemyIndex = null };
        var eff = new CardEffect("debuff", EffectScope.Single, EffectSide.Enemy, 1, Name: "weak");
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(0, next.Enemies[0].GetStatus("weak"));
        Assert.Empty(evs);
    }

    [Fact] public void Buff_random_ally_uses_rng()
    {
        var hero = BattleFixtures.Hero();
        var s = State(hero, BattleFixtures.Goblin());
        var eff = new CardEffect("buff", EffectScope.Random, EffectSide.Ally, 1, Name: "strength");
        var (next, _) = EffectApplier.Apply(s, hero, eff, Rng(0), BattleFixtures.MinimalCatalog());
        Assert.Equal(1, next.Allies[0].GetStatus("strength"));
    }

    [Fact] public void ApplyStatus_event_caster_is_effect_caster()
    {
        var hero = BattleFixtures.Hero();
        var goblin = BattleFixtures.Goblin();
        var s = State(hero, goblin);
        var eff = new CardEffect("debuff", EffectScope.Single, EffectSide.Enemy, 1, Name: "vulnerable");
        var (_, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(hero.InstanceId, evs[0].CasterInstanceId);
        Assert.Equal(goblin.InstanceId, evs[0].TargetInstanceId);
    }

    [Fact] public void Buff_random_with_null_side_throws()
    {
        var hero = BattleFixtures.Hero();
        var s = State(hero, BattleFixtures.Goblin());
        var eff = new CardEffect("buff", EffectScope.Random, null, 1, Name: "strength");
        Assert.Throws<System.InvalidOperationException>(() => EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog()));
    }

    [Fact] public void Buff_all_with_null_side_throws()
    {
        var hero = BattleFixtures.Hero();
        var s = State(hero, BattleFixtures.Goblin());
        var eff = new CardEffect("buff", EffectScope.All, null, 1, Name: "strength");
        Assert.Throws<System.InvalidOperationException>(() => EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog()));
    }

    [Fact] public void Buff_single_ally_targets_target_ally_index()
    {
        var hero = BattleFixtures.Hero();
        var s = State(hero, BattleFixtures.Goblin());
        var eff = new CardEffect("buff", EffectScope.Single, EffectSide.Ally, 2, Name: "strength");
        var (next, _) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(2, next.Allies[0].GetStatus("strength"));
    }

    [Fact] public void Buff_all_allies_adds_to_each()
    {
        var hero = BattleFixtures.Hero();
        var s = State(hero, BattleFixtures.Goblin());
        var eff = new CardEffect("buff", EffectScope.All, EffectSide.Ally, 1, Name: "strength");
        var (next, _) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(1, next.Allies[0].GetStatus("strength"));
    }

    // ---- Phase 10.6.B follow-up: signed strength / dexterity ----

    [Fact] public void Debuff_negative_strength_to_self_stores_negative_value()
    {
        var hero = BattleFixtures.Hero();
        var s = State(hero, BattleFixtures.Goblin());
        // 「自分に筋力 -2 を付与」 (例: ショートショートソード OnBattleStart)
        var eff = new CardEffect("debuff", EffectScope.Self, null, -2, Name: "strength");
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(-2, next.Allies[0].GetStatus("strength"));
        Assert.Single(evs);
        Assert.Equal(BattleEventKind.ApplyStatus, evs[0].Kind);
    }

    [Fact] public void Buff_offsets_negative_strength_back_to_zero_removes_status()
    {
        var hero = BattleFixtures.Hero() with {
            Statuses = ImmutableDictionary<string, int>.Empty.Add("strength", -3)
        };
        var s = State(hero, BattleFixtures.Goblin());
        // 既存 -3 に対して buff strength +3 → 0 → status remove
        var eff = new CardEffect("buff", EffectScope.Self, null, 3, Name: "strength");
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(0, next.Allies[0].GetStatus("strength"));
        Assert.False(next.Allies[0].Statuses.ContainsKey("strength"));
        Assert.Single(evs);
        Assert.Equal(BattleEventKind.RemoveStatus, evs[0].Kind);
    }

    [Fact] public void Stacking_negative_strength_accumulates()
    {
        var hero = BattleFixtures.Hero() with {
            Statuses = ImmutableDictionary<string, int>.Empty.Add("strength", -2)
        };
        var s = State(hero, BattleFixtures.Goblin());
        var eff = new CardEffect("debuff", EffectScope.Self, null, -3, Name: "strength");
        var (next, _) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(-5, next.Allies[0].GetStatus("strength"));
    }

    [Fact] public void Negative_dexterity_can_be_applied_to_self()
    {
        var hero = BattleFixtures.Hero();
        var s = State(hero, BattleFixtures.Goblin());
        var eff = new CardEffect("debuff", EffectScope.Self, null, -1, Name: "dexterity");
        var (next, _) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(-1, next.Allies[0].GetStatus("dexterity"));
    }

    [Fact] public void Negative_weak_is_clamped_to_zero_remove()
    {
        // weak は signed ではなく (turn count)、負値は許容しない (従来通り 0 以下で remove)
        var hero = BattleFixtures.Hero();
        var s = State(hero, BattleFixtures.Goblin());
        var eff = new CardEffect("debuff", EffectScope.Self, null, -1, Name: "weak");
        var (next, _) = EffectApplier.Apply(s, hero, eff, Rng(), BattleFixtures.MinimalCatalog());
        Assert.Equal(0, next.Allies[0].GetStatus("weak"));
        Assert.False(next.Allies[0].Statuses.ContainsKey("weak"));
    }
}
