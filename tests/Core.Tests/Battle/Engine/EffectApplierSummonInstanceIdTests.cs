using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class EffectApplierSummonInstanceIdTests
{
    private static BattleState MakeStateWithHero()
    {
        return new BattleState(
            Turn: 1,
            Phase: BattlePhase.PlayerInput,
            Outcome: RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending,
            Allies: ImmutableArray.Create(BattleFixtures.Hero()),
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

    // FakeRng(int[], double[]) — int 配列は NextInt(0, 1<<30) の範囲内の値を指定する
    private static FakeRng MakeRng(int firstInt) =>
        new FakeRng(new int[] { firstInt }, System.Array.Empty<double>());

    [Fact]
    public void Summon_InstanceId_starts_with_summon_inst_turn_prefix()
    {
        var state = MakeStateWithHero();
        var catalog = BattleFixtures.MinimalCatalog();
        var hero = state.Allies[0];
        var effect = new CardEffect("summon", EffectScope.Self, null, 0, UnitId: "minion");
        var rng = MakeRng(0xABCD);

        var (afterState, _) = EffectApplier.Apply(state, hero, effect, rng, catalog);

        Assert.Equal(2, afterState.Allies.Length);
        var summon = afterState.Allies[1];
        Assert.StartsWith("summon_inst_1_", summon.InstanceId);
    }

    [Fact]
    public void Two_consecutive_summons_in_same_turn_have_unique_InstanceIds()
    {
        // 同 FakeRng で 2 連続呼出 → NextInt が異なる値を返すので ID も異なる
        // int 配列に異なる値を 2 つ用意
        var state = MakeStateWithHero();
        var catalog = BattleFixtures.MinimalCatalog();
        var hero = state.Allies[0];
        var effect = new CardEffect("summon", EffectScope.Self, null, 0, UnitId: "minion");
        var rng = new FakeRng(new int[] { 0x1111, 0x2222 }, System.Array.Empty<double>());

        // 1回目の召喚
        var (state2, _) = EffectApplier.Apply(state, hero, effect, rng, catalog);
        // 2回目の召喚（state2 には既に召喚が1体いる）
        var hero2 = state2.Allies[0];
        var (state3, _) = EffectApplier.Apply(state2, hero2, effect, rng, catalog);

        Assert.Equal(3, state3.Allies.Length);
        var id1 = state2.Allies[1].InstanceId;
        var id2 = state3.Allies[2].InstanceId;
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void Summon_InstanceId_is_deterministic_for_same_seed()
    {
        // 2 つの同内容 FakeRng で同状態 + 同 effect → 同 InstanceId
        var state = MakeStateWithHero();
        var catalog = BattleFixtures.MinimalCatalog();
        var hero = state.Allies[0];
        var effect = new CardEffect("summon", EffectScope.Self, null, 0, UnitId: "minion");

        var rng1 = MakeRng(0x5555);
        var rng2 = MakeRng(0x5555);

        var (afterState1, _) = EffectApplier.Apply(state, hero, effect, rng1, catalog);
        var (afterState2, _) = EffectApplier.Apply(state, hero, effect, rng2, catalog);

        Assert.Equal(afterState1.Allies[1].InstanceId, afterState2.Allies[1].InstanceId);
    }

    [Fact]
    public void Summon_InstanceId_differs_for_different_seeds()
    {
        // 異なる int 配列の FakeRng で → 異なる InstanceId
        var state = MakeStateWithHero();
        var catalog = BattleFixtures.MinimalCatalog();
        var hero = state.Allies[0];
        var effect = new CardEffect("summon", EffectScope.Self, null, 0, UnitId: "minion");

        var rng1 = MakeRng(0x1000);
        var rng2 = MakeRng(0x2000);

        var (afterState1, _) = EffectApplier.Apply(state, hero, effect, rng1, catalog);
        var (afterState2, _) = EffectApplier.Apply(state, hero, effect, rng2, catalog);

        Assert.NotEqual(afterState1.Allies[1].InstanceId, afterState2.Allies[1].InstanceId);
    }
}
