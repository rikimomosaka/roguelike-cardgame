using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Definitions;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class SummonResolverScopeTests
{
    private static BattleState State(
        CombatActor summon, CombatActor[] enemies, int? targetEnemyIndex = 0) => new(
        Turn: 1,
        Phase: BattlePhase.PlayerAttacking,
        Outcome: BattleOutcome.Pending,
        Allies: ImmutableArray.Create(BattleFixtures.Hero(), summon),
        Enemies: enemies.ToImmutableArray(),
        TargetAllyIndex: 0,
        TargetEnemyIndex: targetEnemyIndex,
        Energy: 0,
        EnergyMax: 3,
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

    private static UnitDefinition UnitWithAttackEffects(
        string id, params CardEffect[] effects) =>
        new(id, id, $"img_{id}", 10, "attack",
            new[] { new MoveDefinition("attack", MoveKind.Attack, effects, "attack") },
            LifetimeTurns: null);

    private static IRng Rng(params int[] values) =>
        new FakeRng(values, System.Array.Empty<double>());

    [Fact]
    public void Summon_single_hits_only_the_selected_enemy()
    {
        var summon = BattleFixtures.SummonActor(
            "wisp_inst", "wisp", slotIndex: 1, hp: 10, moveId: "attack");
        var unit = UnitWithAttackEffects("wisp",
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 4));
        var enemy0 = BattleFixtures.Goblin(slotIndex: 0);
        var enemy1 = BattleFixtures.Goblin(slotIndex: 1);

        var (next, events) = PlayerAttackingResolver.Resolve(
            State(summon, new[] { enemy0, enemy1 }, targetEnemyIndex: 1),
            Rng(),
            BattleFixtures.MinimalCatalog(units: new[] { unit }));

        Assert.Equal(20, next.Enemies.Single(e => e.InstanceId == enemy0.InstanceId).CurrentHp);
        Assert.Equal(16, next.Enemies.Single(e => e.InstanceId == enemy1.InstanceId).CurrentHp);
        var hit = Assert.Single(events, e => e.Kind == BattleEventKind.DealDamage);
        Assert.Equal(enemy1.InstanceId, hit.TargetInstanceId);
        Assert.Equal(0, hit.HitIndex);
        Assert.Equal("single", hit.Note);
    }

    [Fact]
    public void Summon_single_does_not_retarget_after_its_fixed_target_dies()
    {
        var summon = BattleFixtures.SummonActor(
            "blade_inst", "blade", slotIndex: 1, hp: 10, moveId: "attack");
        var unit = UnitWithAttackEffects("blade",
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 3),
            new CardEffect("attack", EffectScope.Single, EffectSide.Enemy, 9));
        var survivor = BattleFixtures.Goblin(slotIndex: 0);
        var target = BattleFixtures.Goblin(slotIndex: 1, hp: 3);

        var (next, events) = PlayerAttackingResolver.Resolve(
            State(summon, new[] { survivor, target }, targetEnemyIndex: 1),
            Rng(),
            BattleFixtures.MinimalCatalog(units: new[] { unit }));

        Assert.Equal(20, next.Enemies.Single(e => e.InstanceId == survivor.InstanceId).CurrentHp);
        Assert.Equal(0, next.Enemies.Single(e => e.InstanceId == target.InstanceId).CurrentHp);
        Assert.Single(events, e => e.Kind == BattleEventKind.DealDamage);
    }

    [Fact]
    public void Summon_random_draws_again_for_each_attack_effect()
    {
        var summon = BattleFixtures.SummonActor(
            "chaos_inst", "chaos", slotIndex: 1, hp: 10, moveId: "attack");
        var unit = UnitWithAttackEffects("chaos",
            new CardEffect("attack", EffectScope.Random, EffectSide.Enemy, 3),
            new CardEffect("attack", EffectScope.Random, EffectSide.Enemy, 4));
        var enemy0 = BattleFixtures.Goblin(slotIndex: 0);
        var enemy1 = BattleFixtures.Goblin(slotIndex: 1);

        var (next, events) = PlayerAttackingResolver.Resolve(
            State(summon, new[] { enemy0, enemy1 }),
            Rng(1, 0),
            BattleFixtures.MinimalCatalog(units: new[] { unit }));

        Assert.Equal(16, next.Enemies.Single(e => e.InstanceId == enemy0.InstanceId).CurrentHp);
        Assert.Equal(17, next.Enemies.Single(e => e.InstanceId == enemy1.InstanceId).CurrentHp);
        Assert.Equal(
            new int?[] { 0, 1 },
            events.Where(e => e.Kind == BattleEventKind.DealDamage).Select(e => e.HitIndex));
    }

    [Fact]
    public void Summon_random_excludes_an_enemy_killed_by_the_previous_hit()
    {
        var summon = BattleFixtures.SummonActor(
            "chaos_inst", "chaos", slotIndex: 1, hp: 10, moveId: "attack");
        var unit = UnitWithAttackEffects("chaos",
            new CardEffect("attack", EffectScope.Random, EffectSide.Enemy, 3),
            new CardEffect("attack", EffectScope.Random, EffectSide.Enemy, 4));
        var survivor = BattleFixtures.Goblin(slotIndex: 0);
        var doomed = BattleFixtures.Goblin(slotIndex: 1, hp: 3);

        var (next, _) = PlayerAttackingResolver.Resolve(
            State(summon, new[] { survivor, doomed }),
            Rng(1, 0),
            BattleFixtures.MinimalCatalog(units: new[] { unit }));

        Assert.Equal(16, next.Enemies.Single(e => e.InstanceId == survivor.InstanceId).CurrentHp);
        Assert.Equal(0, next.Enemies.Single(e => e.InstanceId == doomed.InstanceId).CurrentHp);
    }

    [Fact]
    public void Summon_all_applies_each_hit_to_every_enemy_still_alive()
    {
        var summon = BattleFixtures.SummonActor(
            "storm_inst", "storm", slotIndex: 1, hp: 10, moveId: "attack");
        var unit = UnitWithAttackEffects("storm",
            new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 3),
            new CardEffect("attack", EffectScope.All, EffectSide.Enemy, 4));
        var survivor = BattleFixtures.Goblin(slotIndex: 0);
        var doomed = BattleFixtures.Goblin(slotIndex: 1, hp: 3);

        var (next, events) = PlayerAttackingResolver.Resolve(
            State(summon, new[] { survivor, doomed }),
            Rng(),
            BattleFixtures.MinimalCatalog(units: new[] { unit }));

        Assert.Equal(13, next.Enemies.Single(e => e.InstanceId == survivor.InstanceId).CurrentHp);
        Assert.Equal(0, next.Enemies.Single(e => e.InstanceId == doomed.InstanceId).CurrentHp);
        Assert.Equal(
            new int?[] { 0, 0, 1 },
            events.Where(e => e.Kind == BattleEventKind.DealDamage).Select(e => e.HitIndex));
    }
}
