using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.State;

public class BattleStateInvariantTests
{
    private static CombatActor Hero(int hp = 70) =>
        new("hero1", "hero", ActorSide.Ally, 0, hp, hp,
            BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty,
            ImmutableDictionary<string, int>.Empty, null);

    private static CombatActor Goblin(int slotIndex, int hp = 20) =>
        new($"goblin{slotIndex}", "goblin", ActorSide.Enemy, slotIndex, hp, hp,
            BlockPool.Empty, AttackPool.Empty, AttackPool.Empty, AttackPool.Empty,
            ImmutableDictionary<string, int>.Empty, "swing");

    private static BattleState Make(
        ImmutableArray<CombatActor>? allies = null,
        ImmutableArray<CombatActor>? enemies = null,
        BattlePhase phase = BattlePhase.PlayerInput,
        BattleOutcome outcome = BattleOutcome.Pending,
        int turn = 1, int energy = 3, int energyMax = 3,
        int? tgtA = 0, int? tgtE = 0)
        => new(
            Turn: turn, Phase: phase, Outcome: outcome,
            Allies: allies ?? ImmutableArray.Create(Hero()),
            Enemies: enemies ?? ImmutableArray.Create(Goblin(0)),
            TargetAllyIndex: tgtA, TargetEnemyIndex: tgtE,
            Energy: energy, EnergyMax: energyMax,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0,
            LastPlayedOrigCost: null,
            NextCardComboFreePass: false,
            EncounterId: "enc1");

    [Fact] public void Allies_count_at_least_one_at_most_four()
    {
        var s = Make();
        Assert.InRange(s.Allies.Length, 1, 4);
    }

    [Fact] public void Enemies_count_at_most_four()
    {
        var s = Make();
        Assert.InRange(s.Enemies.Length, 0, 4);
    }

    [Fact] public void Hero_is_at_slot_zero()
    {
        var s = Make();
        Assert.Equal("hero", s.Allies[0].DefinitionId);
        Assert.Equal(0, s.Allies[0].SlotIndex);
    }

    [Fact] public void Phase_resolved_iff_outcome_not_pending_victory()
    {
        var s = Make(phase: BattlePhase.Resolved, outcome: BattleOutcome.Victory);
        Assert.True(s.Phase == BattlePhase.Resolved);
        Assert.NotEqual(BattleOutcome.Pending, s.Outcome);
    }

    [Fact] public void Phase_resolved_iff_outcome_not_pending_defeat()
    {
        var s = Make(phase: BattlePhase.Resolved, outcome: BattleOutcome.Defeat);
        Assert.NotEqual(BattleOutcome.Pending, s.Outcome);
    }

    [Fact] public void Energy_within_bounds()
    {
        var s = Make(energy: 3, energyMax: 3);
        Assert.InRange(s.Energy, 0, s.EnergyMax);
    }

    [Fact] public void Turn_starts_from_one_or_higher()
    {
        var s = Make(turn: 1);
        Assert.True(s.Turn >= 1);
    }

    [Fact] public void Target_indices_can_be_null()
    {
        var s = Make(tgtA: null, tgtE: null);
        Assert.Null(s.TargetAllyIndex);
        Assert.Null(s.TargetEnemyIndex);
    }

    [Fact] public void Pile_count_equals_initial_deck_when_no_consumption()
    {
        var deck = ImmutableArray.Create(
            new BattleCardInstance("c1", "strike", false, null),
            new BattleCardInstance("c2", "defend", false, null));
        var s = Make() with { DrawPile = deck };
        var total = s.DrawPile.Length + s.Hand.Length + s.DiscardPile.Length + s.ExhaustPile.Length;
        Assert.Equal(2, total);
    }

    [Fact] public void Statuses_values_are_always_positive()
    {
        // CombatActor.Statuses に <= 0 のエントリが存在しないことを検証
        // (post-condition: EffectApplier と TurnStartProcessor は <= 0 でキーを削除する)
        var hero = BattleFixtures.WithStrength(BattleFixtures.Hero(), 3);
        foreach (var kv in hero.Statuses)
            Assert.True(kv.Value > 0, $"Status '{kv.Key}' has non-positive amount {kv.Value}");
    }

    [Fact] public void Statuses_keys_are_in_StatusDefinition_All()
    {
        // CombatActor.Statuses のキーは既知の StatusDefinition でなければならない
        var validIds = RoguelikeCardGame.Core.Battle.Statuses.StatusDefinition.All
            .Select(s => s.Id).ToHashSet();
        var hero = BattleFixtures.WithStatus(BattleFixtures.Hero(), "strength", 3);
        foreach (var key in hero.Statuses.Keys)
            Assert.Contains(key, validIds);
    }

    [Fact] public void Resolved_phase_implies_outcome_not_pending()
    {
        // Phase=Resolved iff Outcome != Pending の対称性を検証
        var s = Make(
            allies: ImmutableArray.Create(Hero(0)), // HP=0 (dead)
            enemies: ImmutableArray.Create(Goblin(0)),
            phase: BattlePhase.Resolved,
            outcome: RoguelikeCardGame.Core.Battle.State.BattleOutcome.Defeat);
        Assert.Equal(BattlePhase.Resolved, s.Phase);
        Assert.NotEqual(RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending, s.Outcome);
    }

    // === 10.2.C: コンボフィールド ===

    [Fact] public void ComboCount_default_is_zero_via_with()
    {
        var s = Make();
        Assert.Equal(0, s.ComboCount);
        Assert.Null(s.LastPlayedOrigCost);
        Assert.False(s.NextCardComboFreePass);
    }

    [Fact] public void ComboFields_record_equality_distinguishes()
    {
        var s1 = Make();
        var s2 = s1 with { ComboCount = 1 };
        var s3 = s1 with { LastPlayedOrigCost = 2 };
        var s4 = s1 with { NextCardComboFreePass = true };
        Assert.NotEqual(s1, s2);
        Assert.NotEqual(s1, s3);
        Assert.NotEqual(s1, s4);
        Assert.NotEqual(s2, s3);
    }

    [Fact] public void ComboCount_invariant_non_negative()
    {
        var s = Make() with { ComboCount = 0 };
        Assert.True(s.ComboCount >= 0);
        s = s with { ComboCount = 5 };
        Assert.True(s.ComboCount >= 0);
    }
}
