using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

public class DrawHelperTests
{
    // FakeRng には int[] と double[] が必要。シャッフル用に十分な 0 を詰めた配列を用意する。
    private static FakeRng ZeroRng(int intCount = 50) =>
        new FakeRng(new int[intCount], new double[0]);

    private static BattleState MakeStateWithPiles(
        ImmutableArray<BattleCardInstance> draw,
        ImmutableArray<BattleCardInstance> hand,
        ImmutableArray<BattleCardInstance> discard)
    {
        return new BattleState(
            Turn: 1,
            Phase: BattlePhase.PlayerInput,
            Outcome: RoguelikeCardGame.Core.Battle.State.BattleOutcome.Pending,
            Allies: ImmutableArray.Create(BattleFixtures.Hero()),
            Enemies: ImmutableArray.Create(BattleFixtures.Goblin()),
            TargetAllyIndex: 0, TargetEnemyIndex: 0,
            Energy: 3, EnergyMax: 3,
            DrawPile: draw, Hand: hand,
            DiscardPile: discard,
            ExhaustPile: ImmutableArray<BattleCardInstance>.Empty,
            SummonHeld: ImmutableArray<BattleCardInstance>.Empty,
            PowerCards: ImmutableArray<BattleCardInstance>.Empty,
            ComboCount: 0, LastPlayedOrigCost: null, NextCardComboFreePass: false,
            EncounterId: "enc_test");
    }

    [Fact]
    public void Draw_takes_from_top_of_DrawPile()
    {
        var c1 = BattleFixtures.MakeBattleCard("strike", "c1");
        var c2 = BattleFixtures.MakeBattleCard("strike", "c2");
        var state = MakeStateWithPiles(
            ImmutableArray.Create(c1, c2),
            ImmutableArray<BattleCardInstance>.Empty,
            ImmutableArray<BattleCardInstance>.Empty);

        var result = DrawHelper.Draw(state, 1, ZeroRng(), out int drawn);

        Assert.Equal(1, drawn);
        Assert.Single(result.Hand);
        Assert.Equal("c1", result.Hand[0].InstanceId);
        Assert.Single(result.DrawPile);
        Assert.Equal("c2", result.DrawPile[0].InstanceId);
    }

    [Fact]
    public void Draw_shuffles_discard_into_draw_when_drawpile_empty()
    {
        var c1 = BattleFixtures.MakeBattleCard("strike", "c1");
        var c2 = BattleFixtures.MakeBattleCard("strike", "c2");
        var state = MakeStateWithPiles(
            ImmutableArray<BattleCardInstance>.Empty,
            ImmutableArray<BattleCardInstance>.Empty,
            ImmutableArray.Create(c1, c2));

        var result = DrawHelper.Draw(state, 2, ZeroRng(), out int drawn);

        Assert.Equal(2, drawn);
        Assert.Equal(2, result.Hand.Length);
        Assert.Empty(result.DiscardPile);
    }

    [Fact]
    public void Draw_caps_at_HandCap_10()
    {
        var hand = Enumerable.Range(0, 10)
            .Select(i => BattleFixtures.MakeBattleCard("strike", $"h{i}"))
            .ToImmutableArray();
        var draw = ImmutableArray.Create(BattleFixtures.MakeBattleCard("strike", "extra"));
        var state = MakeStateWithPiles(draw, hand, ImmutableArray<BattleCardInstance>.Empty);

        var result = DrawHelper.Draw(state, 5, ZeroRng(), out int drawn);

        Assert.Equal(0, drawn);
        Assert.Equal(10, result.Hand.Length);
    }

    [Fact]
    public void Draw_returns_actuallyDrawn_when_count_exceeds_available()
    {
        var c1 = BattleFixtures.MakeBattleCard("strike", "c1");
        var state = MakeStateWithPiles(
            ImmutableArray.Create(c1),
            ImmutableArray<BattleCardInstance>.Empty,
            ImmutableArray<BattleCardInstance>.Empty);

        var result = DrawHelper.Draw(state, 5, ZeroRng(), out int drawn);

        Assert.Equal(1, drawn);
        Assert.Single(result.Hand);
    }

    [Fact]
    public void Draw_zero_count_is_noop()
    {
        var c1 = BattleFixtures.MakeBattleCard("strike", "c1");
        var state = MakeStateWithPiles(
            ImmutableArray.Create(c1),
            ImmutableArray<BattleCardInstance>.Empty,
            ImmutableArray<BattleCardInstance>.Empty);

        var result = DrawHelper.Draw(state, 0, ZeroRng(), out int drawn);

        Assert.Equal(0, drawn);
        Assert.Empty(result.Hand);
        Assert.Single(result.DrawPile);
    }

    [Fact]
    public void Draw_both_piles_empty_returns_zero()
    {
        var state = MakeStateWithPiles(
            ImmutableArray<BattleCardInstance>.Empty,
            ImmutableArray<BattleCardInstance>.Empty,
            ImmutableArray<BattleCardInstance>.Empty);

        var result = DrawHelper.Draw(state, 5, ZeroRng(), out int drawn);

        Assert.Equal(0, drawn);
    }

    // 5枚シャッフル時に Fisher-Yates が呼ぶ NextInt の回数と範囲:
    //   j=4: NextInt(0,5)、j=3: NextInt(0,4)、j=2: NextInt(0,3)、j=1: NextInt(0,2) → 計4回
    // seqA=[2,1,0,1] は全て各範囲内の有効値で、結果は [c4,c3,c0,c1,c2] 順になる
    // seqB=[0,0,0,0] は全て0で、結果は [c1,c2,c3,c4,c0] 順になる（seqA と異なる）

    [Fact]
    public void Draw_is_deterministic_for_same_rng_sequence()
    {
        // 非自明な int 配列: 5枚シャッフルに必要な4回分の有効値
        var seqA = new[] { 2, 1, 0, 1 };

        var cards = Enumerable.Range(0, 5)
            .Select(i => BattleFixtures.MakeBattleCard("strike", $"c{i}"))
            .ToImmutableArray();
        var state1 = MakeStateWithPiles(
            ImmutableArray<BattleCardInstance>.Empty,
            ImmutableArray<BattleCardInstance>.Empty, cards);
        var state2 = MakeStateWithPiles(
            ImmutableArray<BattleCardInstance>.Empty,
            ImmutableArray<BattleCardInstance>.Empty, cards);

        // 同一 RNG シーケンス (seqA) を 2 回構築して実行 → Hand 順が完全一致する
        var r1 = DrawHelper.Draw(state1, 5, new FakeRng(seqA, System.Array.Empty<double>()), out _);
        var r2 = DrawHelper.Draw(state2, 5, new FakeRng(seqA, System.Array.Empty<double>()), out _);

        Assert.Equal(
            r1.Hand.Select(c => c.InstanceId),
            r2.Hand.Select(c => c.InstanceId));
    }

    [Fact]
    public void Draw_produces_different_order_for_different_rng_sequence()
    {
        // seqA と seqB は異なる並び順を生成することで、実装が RNG を本当に使っていることを確認
        var seqA = new[] { 2, 1, 0, 1 };
        var seqB = new[] { 0, 0, 0, 0 };

        var cards = Enumerable.Range(0, 5)
            .Select(i => BattleFixtures.MakeBattleCard("strike", $"c{i}"))
            .ToImmutableArray();
        var stateA = MakeStateWithPiles(
            ImmutableArray<BattleCardInstance>.Empty,
            ImmutableArray<BattleCardInstance>.Empty, cards);
        var stateB = MakeStateWithPiles(
            ImmutableArray<BattleCardInstance>.Empty,
            ImmutableArray<BattleCardInstance>.Empty, cards);

        var rA = DrawHelper.Draw(stateA, 5, new FakeRng(seqA, System.Array.Empty<double>()), out _);
        var rB = DrawHelper.Draw(stateB, 5, new FakeRng(seqB, System.Array.Empty<double>()), out _);

        // 異なる RNG シーケンスは異なる Hand 順を生成する（rng 依存性の確認）
        Assert.NotEqual(
            rA.Hand.Select(c => c.InstanceId),
            rB.Hand.Select(c => c.InstanceId));
    }
}
