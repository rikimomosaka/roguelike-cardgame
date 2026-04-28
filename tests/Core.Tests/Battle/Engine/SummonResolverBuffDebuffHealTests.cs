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

/// <summary>
/// 召喚 ally の Move が attack/block 以外の effect (buff/debuff/heal) も
/// 実体化することを検証する。symmetric to EnemyAttackingResolverBuffDebuffHealTests。
/// </summary>
public class SummonResolverBuffDebuffHealTests
{
    private static BattleState State(CombatActor[] allies, params CombatActor[] enemies) => new(
        Turn: 1, Phase: BattlePhase.PlayerAttacking,
        Outcome: RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending,
        Allies: allies.ToImmutableArray(),
        Enemies: enemies.ToImmutableArray(),
        TargetAllyIndex: 0, TargetEnemyIndex: 0,
        Energy: 0, EnergyMax: 3,
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

    private static IRng Rng() => new FakeRng(new int[0], new double[0]);

    private static UnitDefinition UnitWithMove(
        string id, string moveId, MoveKind kind, int hp, params CardEffect[] effects) =>
        new(id, id, $"img_{id}", hp, moveId,
            new[] { new MoveDefinition(moveId, kind, effects, moveId) },
            LifetimeTurns: null);

    [Fact]
    public void Summon_buff_self_strength_boosts_casting_summon()
    {
        // 召喚 ally が自分に strength を付ける
        var hero = BattleFixtures.Hero(70);
        var summon = BattleFixtures.SummonActor("wisp_inst", "wisp", 1, hp: 10, moveId: "focus");
        var unitDef = UnitWithMove("wisp", "focus", MoveKind.Buff, hp: 10,
            new CardEffect("buff", EffectScope.Self, null, 1, Name: "strength"));
        var goblinDef = BattleFixtures.GoblinDef(hp: 20, attack: 1);
        var catalog = BattleFixtures.MinimalCatalog(
            enemies: new[] { goblinDef }, units: new[] { unitDef });

        var (next, events) = PlayerAttackingResolver.Resolve(
            State(new[] { hero, summon }, BattleFixtures.Goblin()), Rng(), catalog);

        var afterSummon = next.Allies.First(a => a.InstanceId == summon.InstanceId);
        Assert.Equal(1, afterSummon.GetStatus("strength"));
        Assert.Contains(events, e => e.Kind == BattleEventKind.ApplyStatus
            && e.TargetInstanceId == summon.InstanceId
            && e.Note == "strength");
    }

    [Fact]
    public void Summon_debuff_all_side_enemy_targets_all_living_enemies()
    {
        // 召喚 ally の debuff "side: enemy" → caster=Ally なので state.Enemies に着弾
        var hero = BattleFixtures.Hero(70);
        var summon = BattleFixtures.SummonActor("hex_inst", "hex", 1, hp: 10, moveId: "curse");
        var unitDef = UnitWithMove("hex", "curse", MoveKind.Debuff, hp: 10,
            new CardEffect("debuff", EffectScope.All, EffectSide.Enemy, 1, Name: "weak"));
        var goblinDef = BattleFixtures.GoblinDef(hp: 20, attack: 1);
        var catalog = BattleFixtures.MinimalCatalog(
            enemies: new[] { goblinDef }, units: new[] { unitDef });

        var goblin1 = BattleFixtures.Goblin(slotIndex: 0);
        var goblin2 = BattleFixtures.Goblin(slotIndex: 1);
        var (next, _) = PlayerAttackingResolver.Resolve(
            State(new[] { hero, summon }, goblin1, goblin2), Rng(), catalog);

        Assert.Equal(1, next.Enemies.First(e => e.InstanceId == goblin1.InstanceId).GetStatus("weak"));
        Assert.Equal(1, next.Enemies.First(e => e.InstanceId == goblin2.InstanceId).GetStatus("weak"));
        // 味方 (hero, summon) には付かない
        foreach (var a in next.Allies)
            Assert.Equal(0, a.GetStatus("weak"));
    }

    [Fact]
    public void Summon_heal_self_increases_caster_hp()
    {
        // 召喚 ally の self-heal
        var hero = BattleFixtures.Hero(70);
        var injured = BattleFixtures.SummonActor("medic_inst", "medic", 1, hp: 20, moveId: "mend")
            with { CurrentHp = 5 };
        var unitDef = UnitWithMove("medic", "mend", MoveKind.Buff, hp: 20,
            new CardEffect("heal", EffectScope.Self, null, 8));
        var goblinDef = BattleFixtures.GoblinDef(hp: 20, attack: 1);
        var catalog = BattleFixtures.MinimalCatalog(
            enemies: new[] { goblinDef }, units: new[] { unitDef });

        var (next, events) = PlayerAttackingResolver.Resolve(
            State(new[] { hero, injured }, BattleFixtures.Goblin()), Rng(), catalog);

        var after = next.Allies.First(a => a.InstanceId == injured.InstanceId);
        Assert.Equal(13, after.CurrentHp);
        Assert.Contains(events, e => e.Kind == BattleEventKind.Heal
            && e.TargetInstanceId == injured.InstanceId
            && e.Amount == 8);
    }
}
