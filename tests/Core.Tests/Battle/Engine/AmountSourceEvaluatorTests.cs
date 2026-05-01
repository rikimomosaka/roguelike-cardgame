using System.Collections.Immutable;
using RoguelikeCardGame.Core.Battle.Engine;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Tests.Battle.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.Engine;

/// <summary>
/// Phase 10.5.D: AmountSourceEvaluator unit tests。
/// 10 種の source 値 + 未知値 throw を網羅。
/// </summary>
public class AmountSourceEvaluatorTests
{
    [Fact]
    public void HandCount_returns_hand_length()
    {
        var hero = BattleFixtures.Hero();
        var hand = ImmutableArray.Create(
            new BattleCardInstance("a", "strike", false, null),
            new BattleCardInstance("b", "defend", false, null));
        var state = BattleFixtures.MakeStateWithHero(hero) with { Hand = hand };

        Assert.Equal(2, AmountSourceEvaluator.Evaluate("handCount", state, hero));
    }

    [Fact]
    public void DrawPileCount_returns_drawPile_length()
    {
        var hero = BattleFixtures.Hero();
        var draw = ImmutableArray.Create(
            new BattleCardInstance("a", "strike", false, null),
            new BattleCardInstance("b", "defend", false, null),
            new BattleCardInstance("c", "bash", false, null));
        var state = BattleFixtures.MakeStateWithHero(hero) with { DrawPile = draw };

        Assert.Equal(3, AmountSourceEvaluator.Evaluate("drawPileCount", state, hero));
    }

    [Fact]
    public void DiscardPileCount_returns_discardPile_length()
    {
        var hero = BattleFixtures.Hero();
        var disc = ImmutableArray.Create(new BattleCardInstance("a", "strike", false, null));
        var state = BattleFixtures.MakeStateWithHero(hero) with { DiscardPile = disc };

        Assert.Equal(1, AmountSourceEvaluator.Evaluate("discardPileCount", state, hero));
    }

    [Fact]
    public void ExhaustPileCount_returns_exhaust_length()
    {
        var hero = BattleFixtures.Hero();
        var ex = ImmutableArray.Create(new BattleCardInstance("a", "strike", false, null));
        var state = BattleFixtures.MakeStateWithHero(hero) with { ExhaustPile = ex };

        Assert.Equal(1, AmountSourceEvaluator.Evaluate("exhaustPileCount", state, hero));
    }

    [Fact]
    public void SelfHp_returns_caster_currentHp()
    {
        var hero = BattleFixtures.Hero(currentHp: 47, maxHp: 80);
        var state = BattleFixtures.MakeStateWithHero(hero);

        Assert.Equal(47, AmountSourceEvaluator.Evaluate("selfHp", state, hero));
    }

    [Fact]
    public void SelfHpLost_returns_maxHp_minus_currentHp()
    {
        var hero = BattleFixtures.Hero(currentHp: 47, maxHp: 80);
        var state = BattleFixtures.MakeStateWithHero(hero);

        Assert.Equal(33, AmountSourceEvaluator.Evaluate("selfHpLost", state, hero));
    }

    [Fact]
    public void ComboCount_returns_state_comboCount()
    {
        var hero = BattleFixtures.Hero();
        var state = BattleFixtures.MakeStateWithHero(hero) with { ComboCount = 4 };

        Assert.Equal(4, AmountSourceEvaluator.Evaluate("comboCount", state, hero));
    }

    [Fact]
    public void Energy_returns_state_energy()
    {
        var hero = BattleFixtures.Hero();
        var state = BattleFixtures.MakeStateWithHero(hero) with { Energy = 2, EnergyMax = 3 };

        Assert.Equal(2, AmountSourceEvaluator.Evaluate("energy", state, hero));
    }

    [Fact]
    public void PowerCardCount_returns_powerCards_length()
    {
        var hero = BattleFixtures.Hero();
        var powers = ImmutableArray.Create(
            new BattleCardInstance("p1", "x", false, null),
            new BattleCardInstance("p2", "y", false, null));
        var state = BattleFixtures.MakeStateWithHero(hero) with { PowerCards = powers };

        Assert.Equal(2, AmountSourceEvaluator.Evaluate("powerCardCount", state, hero));
    }

    [Fact]
    public void SelfBlock_returns_caster_block_display()
    {
        // BlockPool に WithFixed/Total API は無いため、Empty.Add(7) で構築。
        // 既存 BlockDisplay 計算 (Block.Display(dexterity)) と整合させる。
        var hero = BattleFixtures.Hero() with { Block = BlockPool.Empty.Add(7) };
        var state = BattleFixtures.MakeStateWithHero(hero);

        Assert.Equal(7, AmountSourceEvaluator.Evaluate("selfBlock", state, hero));
    }

    [Fact]
    public void SelfBlock_includes_dexterity_addCount_bonus()
    {
        // dexterity=2、Add 呼出 1 回 → Display = 5 + 1*2 = 7
        var hero = BattleFixtures.Hero() with { Block = BlockPool.Empty.Add(5) };
        hero = BattleFixtures.WithDexterity(hero, 2);
        var state = BattleFixtures.MakeStateWithHero(hero);

        Assert.Equal(7, AmountSourceEvaluator.Evaluate("selfBlock", state, hero));
    }

    [Fact]
    public void Unknown_source_throws()
    {
        var hero = BattleFixtures.Hero();
        var state = BattleFixtures.MakeStateWithHero(hero);

        Assert.Throws<System.InvalidOperationException>(() =>
            AmountSourceEvaluator.Evaluate("nonexistentSource", state, hero));
    }
}
