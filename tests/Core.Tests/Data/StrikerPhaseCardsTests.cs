using System.Linq;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Data;

/// <summary>位相を使う最初のストライカーカード 8 枚。</summary>
public sealed class StrikerPhaseCardsTests
{
    [Theory]
    [InlineData("striker_phase_calm_form", "静水の構え", CardType.Skill, 1, new[] { "block", "setPhase" })]
    [InlineData("striker_phase_motion_form", "奔流の構え", CardType.Skill, 1, new[] { "draw", "setPhase" })]
    [InlineData("striker_phase_calm_blade", "静の太刀", CardType.Attack, 1, new[] { "attack", "setPhase" })]
    [InlineData("striker_phase_blazing_step", "烈火の踏み込み", CardType.Attack, 1, new[] { "attack", "setPhase" })]
    [InlineData("striker_phase_flowing_stance", "流転の構え", CardType.Skill, 0, new[] { "invertPhase" })]
    [InlineData("striker_phase_return_edge", "返し刃", CardType.Attack, 1, new[] { "attack", "invertPhase" })]
    [InlineData("striker_phase_honing", "研ぎ澄まし", CardType.Skill, 1, new[] { "augmentCards" })]
    [InlineData("striker_phase_quelling_shout", "鎮めの一喝", CardType.Skill, 1, new[] { "setPhase", "block" })]
    public void PhaseCard_IsStrikerRewardCard(string id, string name, CardType type, int cost, string[] actions)
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();

        Assert.True(catalog.TryGetCard(id, out var card));
        Assert.Equal(name, card.Name);
        Assert.Equal(type, card.CardType);
        Assert.Equal(cost, card.Cost);
        Assert.Equal(CardPoolClass.Striker, card.PoolClass);
        Assert.Contains(CardPoolKind.Reward, card.EffectivePoolKinds);
        Assert.Equal(actions, card.Effects.Select(e => e.Action));
        Assert.True(card.IsUpgradable);
    }

    [Fact]
    public void MotionForm_UpgradeCostsZero()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();

        Assert.Equal(0, catalog.Cards["striker_phase_motion_form"].UpgradedCost);
    }
}
