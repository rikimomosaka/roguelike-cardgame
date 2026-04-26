using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

/// <summary>
/// SummonCleanup の単体テスト。
/// 死亡 summon の AssociatedSummonHeldInstanceId を辿り、
/// 対応カードを SummonHeld → DiscardPile に移動する処理を検証する。
/// 親 spec §5-4 / Phase 10.2.D spec §4-4 参照。
/// </summary>
public class SummonCleanupTests
{
    private static BattleState MakeState(
        ImmutableArray<CombatActor> allies,
        ImmutableArray<BattleCardInstance> summonHeld) =>
        new BattleState(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: allies,
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: 0, EnergyMax: 3,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: summonHeld,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0, LastPlayedOrigCost: null, NextCardComboFreePass: false,
            EncounterId: "enc_test");

    [Fact]
    public void Dead_summon_moves_card_to_discard()
    {
        var hero = BattleFixtures.Hero();
        // 死亡 summon (HP=0) かつ associated card あり
        var summon = BattleFixtures.SummonActor("s1", "minion", 1, hp: 10, associatedCardId: "card1")
            with { CurrentHp = 0 };
        var card = BattleFixtures.MakeBattleCard("minion_card", "card1");
        var s = MakeState(
            ImmutableArray.Create(hero, summon),
            ImmutableArray.Create(card));

        var events = new List<BattleEvent>();
        int order = 0;
        var next = SummonCleanup.Apply(s, events, ref order);

        Assert.Empty(next.SummonHeld);
        Assert.Single(next.DiscardPile);
        Assert.Equal("card1", next.DiscardPile[0].InstanceId);
        // ally の AssociatedSummonHeldInstanceId が null 化されている
        var summonNext = next.Allies.Single(a => a.InstanceId == "s1");
        Assert.Null(summonNext.AssociatedSummonHeldInstanceId);
    }

    [Fact]
    public void Alive_summon_not_processed()
    {
        var hero = BattleFixtures.Hero();
        var summon = BattleFixtures.SummonActor("s1", "minion", 1, hp: 10, associatedCardId: "card1");
        var card = BattleFixtures.MakeBattleCard("minion_card", "card1");
        var s = MakeState(
            ImmutableArray.Create(hero, summon),
            ImmutableArray.Create(card));

        var events = new List<BattleEvent>();
        int order = 0;
        var next = SummonCleanup.Apply(s, events, ref order);

        Assert.Single(next.SummonHeld);
        Assert.Empty(next.DiscardPile);
        var summonNext = next.Allies.Single(a => a.InstanceId == "s1");
        Assert.Equal("card1", summonNext.AssociatedSummonHeldInstanceId);
    }

    [Fact]
    public void Dead_hero_not_processed()
    {
        // hero は AssociatedSummonHeldInstanceId が null（spec invariant）。
        // 仮に hero が死亡しても cleanup の対象にならない。
        var hero = BattleFixtures.Hero() with { CurrentHp = 0 };
        var s = MakeState(
            ImmutableArray.Create(hero),
            ImmutableArray<BattleCardInstance>.Empty);

        var events = new List<BattleEvent>();
        int order = 0;
        var next = SummonCleanup.Apply(s, events, ref order);

        Assert.Empty(next.SummonHeld);
        Assert.Empty(next.DiscardPile);
        Assert.Equal(s, next);
    }

    [Fact]
    public void Already_cleaned_summon_not_reprocessed()
    {
        // 死亡しているが AssociatedSummonHeldInstanceId が既に null（過去 cleanup 済み）
        var hero = BattleFixtures.Hero();
        var summon = BattleFixtures.SummonActor("s1", "minion", 1, hp: 10, associatedCardId: null)
            with { CurrentHp = 0 };
        var s = MakeState(
            ImmutableArray.Create(hero, summon),
            ImmutableArray<BattleCardInstance>.Empty);

        var events = new List<BattleEvent>();
        int order = 0;
        var next = SummonCleanup.Apply(s, events, ref order);

        Assert.Empty(next.SummonHeld);
        Assert.Empty(next.DiscardPile);
        Assert.Equal(s, next);
    }

    [Fact]
    public void Multiple_dead_summons_all_processed()
    {
        var hero = BattleFixtures.Hero();
        var summon1 = BattleFixtures.SummonActor("s1", "minion", 1, hp: 10, associatedCardId: "card1")
            with { CurrentHp = 0 };
        var summon2 = BattleFixtures.SummonActor("s2", "minion", 2, hp: 10, associatedCardId: "card2")
            with { CurrentHp = 0 };
        var card1 = BattleFixtures.MakeBattleCard("minion_card", "card1");
        var card2 = BattleFixtures.MakeBattleCard("minion_card", "card2");
        var s = MakeState(
            ImmutableArray.Create(hero, summon1, summon2),
            ImmutableArray.Create(card1, card2));

        var events = new List<BattleEvent>();
        int order = 0;
        var next = SummonCleanup.Apply(s, events, ref order);

        Assert.Empty(next.SummonHeld);
        Assert.Equal(2, next.DiscardPile.Length);
        var ids = next.DiscardPile.Select(c => c.InstanceId).ToList();
        Assert.Contains("card1", ids);
        Assert.Contains("card2", ids);
        Assert.Null(next.Allies.Single(a => a.InstanceId == "s1").AssociatedSummonHeldInstanceId);
        Assert.Null(next.Allies.Single(a => a.InstanceId == "s2").AssociatedSummonHeldInstanceId);
    }
}
