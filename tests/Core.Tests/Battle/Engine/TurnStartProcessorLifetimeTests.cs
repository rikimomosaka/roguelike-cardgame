using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.Events;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class TurnStartProcessorLifetimeTests
{
    private static IRng Rng() => new FakeRng(new int[20], new double[0]);

    private static BattleState MakeState(params CombatActor[] allies)
    {
        var alliesArr = allies.Length == 0
            ? ImmutableArray.Create(BattleFixtures.Hero())
            : ImmutableArray.CreateRange(allies);
        return new BattleState(
            Turn: 1, Phase: BattlePhase.PlayerInput, Outcome: BattleOutcome.Pending,
            Allies: alliesArr,
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: 0, EnergyMax: 3,
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

    [Fact] public void Hero_lifetime_null_skipped()
    {
        var s = MakeState();   // hero only, lifetime=null
        var (next, _) = TurnStartProcessor.Process(s, Rng());
        Assert.Null(next.Allies[0].RemainingLifetimeTurns);
    }

    [Fact] public void Summon_lifetime_3_decrements_to_2()
    {
        var hero = BattleFixtures.Hero();
        var summon = BattleFixtures.SummonActor("s1", "minion", 1, hp: 10, lifetime: 3);
        var s = MakeState(hero, summon);
        var (next, _) = TurnStartProcessor.Process(s, Rng());
        var summonNext = next.Allies.Single(a => a.InstanceId == "s1");
        Assert.Equal(2, summonNext.RemainingLifetimeTurns);
        Assert.True(summonNext.IsAlive);
    }

    [Fact] public void Summon_lifetime_1_dies()
    {
        var hero = BattleFixtures.Hero();
        var summon = BattleFixtures.SummonActor("s1", "minion", 1, hp: 10, lifetime: 1);
        var s = MakeState(hero, summon);
        var (next, evs) = TurnStartProcessor.Process(s, Rng());
        var summonNext = next.Allies.Single(a => a.InstanceId == "s1");
        Assert.Equal(0, summonNext.CurrentHp);
        Assert.False(summonNext.IsAlive);
        Assert.Contains(evs, e => e.Kind == BattleEventKind.ActorDeath
            && e.TargetInstanceId == "s1"
            && e.Note == "lifetime");
    }

    [Fact] public void Lifetime_tick_after_status_countdown()
    {
        // weak と lifetime を併用、weak countdown と lifetime tick の順序を確認
        var hero = BattleFixtures.Hero();
        var summon = BattleFixtures.SummonActor("s1", "minion", 1, hp: 10, lifetime: 2);
        summon = summon with { Statuses = ImmutableDictionary<string, int>.Empty.Add("weak", 1) };
        var s = MakeState(hero, summon);
        var (next, _) = TurnStartProcessor.Process(s, Rng());
        var summonNext = next.Allies.Single(a => a.InstanceId == "s1");
        Assert.Equal(1, summonNext.RemainingLifetimeTurns);   // 2 → 1
        Assert.False(summonNext.Statuses.ContainsKey("weak")); // 1 → 0 → removed
    }
}
