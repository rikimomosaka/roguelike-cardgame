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

public class EffectApplierSummonTests
{
    private static IRng Rng() => new FakeRng(new int[10], new double[0]);

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
            Energy: 3, EnergyMax: 3,
            DrawPile: ImmutableArray<BattleCardInstance>.Empty,
            Hand: ImmutableArray<BattleCardInstance>.Empty,
            DiscardPile: ImmutableArray<BattleCardInstance>.Empty,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0, LastPlayedOrigCost: null, NextCardComboFreePass: false,
            EncounterId: "enc_test");
    }

    [Fact] public void Summon_succeeds_when_slots_available()
    {
        var s = MakeState();   // hero only at slot 0
        var hero = s.Allies[0];
        var eff = new CardEffect("summon", EffectScope.Self, null, 0, UnitId: "minion");
        var cat = BattleFixtures.MinimalCatalog(units: new[] { BattleFixtures.MinionDef() });
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), cat);

        Assert.Equal(2, next.Allies.Length);
        var minion = next.Allies[1];
        Assert.Equal("minion", minion.DefinitionId);
        Assert.Equal(1, minion.SlotIndex);   // 空き最小 slot
        Assert.Equal(10, minion.CurrentHp);
        Assert.Equal(ActorSide.Ally, minion.Side);

        Assert.Single(evs);
        Assert.Equal(BattleEventKind.Summon, evs[0].Kind);
        Assert.Equal("minion", evs[0].Note);
    }

    [Fact] public void Summon_fails_silently_when_slots_full()
    {
        var allies = new[] {
            BattleFixtures.Hero(),
            BattleFixtures.SummonActor("s1", "minion", 1),
            BattleFixtures.SummonActor("s2", "minion", 2),
            BattleFixtures.SummonActor("s3", "minion", 3),
        };
        var s = MakeState(allies);
        var hero = s.Allies[0];
        var eff = new CardEffect("summon", EffectScope.Self, null, 0, UnitId: "minion");
        var cat = BattleFixtures.MinimalCatalog(units: new[] { BattleFixtures.MinionDef() });
        var (next, evs) = EffectApplier.Apply(s, hero, eff, Rng(), cat);

        Assert.Equal(4, next.Allies.Length);   // 不変
        Assert.Empty(evs);
    }

    [Fact] public void Summon_takes_lowest_empty_slot()
    {
        // hero slot 0 + summon slot 2 (slot 1 is empty)
        var allies = new[] {
            BattleFixtures.Hero(),
            BattleFixtures.SummonActor("s2", "minion", 2),
        };
        var s = MakeState(allies);
        var hero = s.Allies[0];
        var eff = new CardEffect("summon", EffectScope.Self, null, 0, UnitId: "minion");
        var cat = BattleFixtures.MinimalCatalog(units: new[] { BattleFixtures.MinionDef() });
        var (next, _) = EffectApplier.Apply(s, hero, eff, Rng(), cat);
        var newMinion = next.Allies.Last();
        Assert.Equal(1, newMinion.SlotIndex);  // 空き最小 = 1
    }

    [Fact] public void Summon_with_lifetime_sets_remaining_turns()
    {
        var s = MakeState();
        var hero = s.Allies[0];
        var eff = new CardEffect("summon", EffectScope.Self, null, 0, UnitId: "ephemeral");
        var unitDef = BattleFixtures.MinionDef(id: "ephemeral", lifetime: 3);
        var cat = BattleFixtures.MinimalCatalog(units: new[] { unitDef });
        var (next, _) = EffectApplier.Apply(s, hero, eff, Rng(), cat);
        Assert.Equal(3, next.Allies[1].RemainingLifetimeTurns);
    }

    [Fact] public void Summon_associated_id_is_null_initially()
    {
        // PlayCard の card-move logic で後設定される。EffectApplier 単体では null
        var s = MakeState();
        var hero = s.Allies[0];
        var eff = new CardEffect("summon", EffectScope.Self, null, 0, UnitId: "minion");
        var cat = BattleFixtures.MinimalCatalog(units: new[] { BattleFixtures.MinionDef() });
        var (next, _) = EffectApplier.Apply(s, hero, eff, Rng(), cat);
        Assert.Null(next.Allies[1].AssociatedSummonHeldInstanceId);
    }

    [Fact] public void Summon_unitId_null_throws()
    {
        var s = MakeState();
        var hero = s.Allies[0];
        var eff = new CardEffect("summon", EffectScope.Self, null, 0, UnitId: null);
        var cat = BattleFixtures.MinimalCatalog(units: new[] { BattleFixtures.MinionDef() });
        Assert.Throws<System.InvalidOperationException>(() =>
            EffectApplier.Apply(s, hero, eff, Rng(), cat));
    }

    [Fact] public void Summon_unknown_unitId_throws()
    {
        var s = MakeState();
        var hero = s.Allies[0];
        var eff = new CardEffect("summon", EffectScope.Self, null, 0, UnitId: "unknown");
        var cat = BattleFixtures.MinimalCatalog(units: new[] { BattleFixtures.MinionDef() });
        Assert.Throws<System.InvalidOperationException>(() =>
            EffectApplier.Apply(s, hero, eff, Rng(), cat));
    }
}
