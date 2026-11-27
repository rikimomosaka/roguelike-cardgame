using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Cards;

public sealed class CardTextFormatterPhaseTests
{
    private static string Format(CardEffect effect) => CardTextFormatter.FormatEffects(new[] { effect });

    [Theory]
    [InlineData("calm", "位相を[P:calm]にする。")]
    [InlineData("motion", "位相を[P:motion]にする。")]
    [InlineData("none", "位相をなしにする。")]
    public void SetPhase_Self(string name, string expected) =>
        Assert.Equal(expected, Format(new CardEffect("setPhase", EffectScope.Self, null, 0, Name: name)));

    [Fact]
    public void SetPhase_SingleEnemy() =>
        Assert.Equal("敵単体の位相をなしにする。",
            Format(new CardEffect("setPhase", EffectScope.Single, EffectSide.Enemy, 0, Name: "none")));

    [Fact]
    public void InvertPhase_Self() =>
        Assert.Equal("位相を[K:invert]する。", Format(new CardEffect("invertPhase", EffectScope.Self, null, 0)));

    [Fact]
    public void AugmentCards_ChooseOneAttack() =>
        Assert.Equal("手札の選んだアタック[N:1]枚に[K:enhance]【[K:bipolar]】する。",
            Format(new CardEffect("augmentCards", EffectScope.Self, null, 1, Name: "bipolar",
                Pile: "hand", Select: "choose", CardCount: 1, CardTypeFilter: "Attack")));

    [Fact]
    public void AugmentCards_AllAttacks() =>
        Assert.Equal("手札の全アタックに[K:enhance]【[K:bipolar]】する。",
            Format(new CardEffect("augmentCards", EffectScope.Self, null, 1, Name: "bipolar",
                Pile: "hand", Select: "all", CardTypeFilter: "Attack")));

    [Fact]
    public void AugmentCards_RandomTwoStacks() =>
        // 双極は上限 1 なので、2 を付与する効果でも【双極】と書く。
        Assert.Equal("手札のランダムなアタック[N:1]枚に[K:enhance]【[K:bipolar]】する。",
            Format(new CardEffect("augmentCards", EffectScope.Self, null, 2, Name: "bipolar",
                Pile: "hand", Select: "random", CardCount: 1, CardTypeFilter: "Attack")));

    [Fact]
    public void AugmentCards_Self() =>
        Assert.Equal("このカードに[K:enhance]【[K:bipolar]】する。",
            Format(new CardEffect("augmentCards", EffectScope.Self, null, 1, Name: "bipolar", Pile: "self")));

    [Fact]
    public void Keywords_EnhanceAndInvert_HaveDescriptions()
    {
        Assert.Equal("【 】内の効果を与える。", CardKeywords.Get("enhance")!.Description);
        Assert.Equal("エンハンス", CardKeywords.Get("enhance")!.Name);
        Assert.Equal("位相を持つ場合、対となる位相に変更する。", CardKeywords.Get("invert")!.Description);
        Assert.Equal("反転", CardKeywords.Get("invert")!.Name);
    }
}
