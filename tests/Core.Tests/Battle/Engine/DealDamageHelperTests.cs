using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class DealDamageHelperTests
{
    [Fact] public void Baseline_no_status_no_block_full_damage()
    {
        var att = BattleFixtures.Hero();
        var tgt = BattleFixtures.Goblin();
        var (updated, evs, died) = DealDamageHelper.Apply(
            attacker: att, target: tgt, baseSum: 6, addCount: 1, scopeNote: "single", orderBase: 0);
        Assert.Equal(20 - 6, updated.CurrentHp);
        Assert.False(died);
        Assert.Equal(2, evs.Count);
        Assert.Equal(BattleEventKind.AttackFire, evs[0].Kind);
        Assert.Equal(6, evs[0].Amount);
        Assert.Equal(BattleEventKind.DealDamage, evs[1].Kind);
        Assert.Equal(6, evs[1].Amount);
    }

    [Fact] public void Strength_adds_per_addcount()
    {
        // baseSum=8, addCount=2, strength=3 → totalAttack = 8 + 2*3 = 14
        var att = BattleFixtures.WithStrength(BattleFixtures.Hero(), 3);
        var tgt = BattleFixtures.Goblin(hp: 30);
        var (updated, evs, _) = DealDamageHelper.Apply(att, tgt, 8, 2, "single", 0);
        Assert.Equal(30 - 14, updated.CurrentHp);
        Assert.Equal(14, evs[0].Amount); // AttackFire
    }

    [Fact] public void Weak_applies_three_quarters_floor()
    {
        // baseSum=10, addCount=1, weak=1 → totalAttack = floor(10 * 0.75) = 7
        var att = BattleFixtures.WithWeak(BattleFixtures.Hero(), 1);
        var tgt = BattleFixtures.Goblin(hp: 30);
        var (updated, evs, _) = DealDamageHelper.Apply(att, tgt, 10, 1, "single", 0);
        Assert.Equal(30 - 7, updated.CurrentHp);
        Assert.Equal(7, evs[0].Amount);
    }

    [Fact] public void Strength_with_weak_combines()
    {
        // baseSum=8, addCount=2, strength=3, weak=1 → boosted=14、× 0.75 = 10
        var att = BattleFixtures.WithWeak(BattleFixtures.WithStrength(BattleFixtures.Hero(), 3), 1);
        var tgt = BattleFixtures.Goblin(hp: 30);
        var (updated, evs, _) = DealDamageHelper.Apply(att, tgt, 8, 2, "single", 0);
        Assert.Equal(30 - 10, updated.CurrentHp);
    }

    [Fact] public void Block_absorbs_damage()
    {
        // baseSum=10, target.Block=Sum=4 (no dex) → absorbed=4, rawDamage=6
        var att = BattleFixtures.Hero();
        var tgt = BattleFixtures.Goblin(hp: 30) with { Block = BlockPool.Empty.Add(4) };
        var (updated, evs, _) = DealDamageHelper.Apply(att, tgt, 10, 1, "single", 0);
        Assert.Equal(30 - 6, updated.CurrentHp);
        Assert.Equal(0, updated.Block.Sum);  // 全消費
        Assert.Equal(0, updated.Block.AddCount);
    }

    [Fact] public void Dexterity_boosts_block()
    {
        // target.Block=Sum=2, AddCount=1, dex=3 → Display=5、attack=4 → absorbed=4, rawDamage=0、残 Block=1
        var att = BattleFixtures.Hero();
        var tgt = BattleFixtures.WithDexterity(BattleFixtures.Goblin(hp: 30), 3) with { Block = BlockPool.Empty.Add(2) };
        var (updated, evs, _) = DealDamageHelper.Apply(att, tgt, 4, 1, "single", 0);
        Assert.Equal(30, updated.CurrentHp);  // 完全吸収
        Assert.Equal(1, updated.Block.Sum);   // 5 - 4 = 1
        Assert.Equal(0, updated.Block.AddCount);
    }

    [Fact] public void Vulnerable_after_block_multiplies_one_point_five()
    {
        // attack=10, Block=4 → rawDamage=6、vuln → floor(6 * 1.5) = 9
        var att = BattleFixtures.Hero();
        var tgt = BattleFixtures.WithVulnerable(BattleFixtures.Goblin(hp: 30), 1) with { Block = BlockPool.Empty.Add(4) };
        var (updated, evs, _) = DealDamageHelper.Apply(att, tgt, 10, 1, "single", 0);
        Assert.Equal(30 - 9, updated.CurrentHp);
        Assert.Equal(10, evs[0].Amount); // AttackFire = totalAttack
        Assert.Equal(9, evs[1].Amount);  // DealDamage = damage 着弾
    }

    [Fact] public void Vulnerable_blocked_completely_no_amplification()
    {
        // attack=10, Block=100 → rawDamage=0、vuln → 0 のまま
        var att = BattleFixtures.Hero();
        var tgt = BattleFixtures.WithVulnerable(BattleFixtures.Goblin(hp: 30), 1) with { Block = BlockPool.Empty.Add(100) };
        var (updated, evs, _) = DealDamageHelper.Apply(att, tgt, 10, 1, "single", 0);
        Assert.Equal(30, updated.CurrentHp);
    }

    [Fact] public void All_corrections_combined()
    {
        // baseSum=8, addCount=2, str=3, weak=1, dex=0, Block=Sum=2、vuln=1
        // totalAttack = floor((8 + 2*3) * 0.75) = floor(14 * 0.75) = 10
        // Block=Sum=2, dex=0 → absorbed=2, rawDamage=8
        // vuln → floor(8 * 1.5) = 12
        var att = BattleFixtures.WithWeak(BattleFixtures.WithStrength(BattleFixtures.Hero(), 3), 1);
        var tgt = BattleFixtures.WithVulnerable(BattleFixtures.Goblin(hp: 30), 1) with { Block = BlockPool.Empty.Add(2) };
        var (updated, evs, _) = DealDamageHelper.Apply(att, tgt, 8, 2, "single", 0);
        Assert.Equal(30 - 12, updated.CurrentHp);
    }

    [Fact] public void Dies_now_emits_actor_death()
    {
        var att = BattleFixtures.Hero();
        var tgt = BattleFixtures.Goblin(hp: 5);
        var (updated, evs, died) = DealDamageHelper.Apply(att, tgt, 10, 1, "single", 0);
        Assert.True(died);
        Assert.Equal(3, evs.Count);
        Assert.Equal(BattleEventKind.ActorDeath, evs[2].Kind);
    }

    [Fact] public void Already_dead_target_does_not_emit_death()
    {
        var att = BattleFixtures.Hero();
        var tgt = BattleFixtures.Goblin(hp: 0);
        var (updated, evs, died) = DealDamageHelper.Apply(att, tgt, 10, 1, "single", 0);
        Assert.False(died);
        Assert.Equal(2, evs.Count); // AttackFire + DealDamage のみ
    }

    [Fact] public void Order_starts_at_orderBase()
    {
        var att = BattleFixtures.Hero();
        var tgt = BattleFixtures.Goblin(hp: 5);
        var (_, evs, _) = DealDamageHelper.Apply(att, tgt, 10, 1, "single", orderBase: 5);
        Assert.Equal(5, evs[0].Order);
        Assert.Equal(6, evs[1].Order);
        Assert.Equal(7, evs[2].Order);
    }
}
