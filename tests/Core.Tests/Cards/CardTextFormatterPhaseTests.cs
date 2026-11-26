using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Cards;

public sealed class CardTextFormatterPhaseTests
{
    private static string Format(CardEffect effect) => CardTextFormatter.FormatEffects(new[] { effect });

    [Theory]
    [InlineData("calm", "位相を静にする。")]
    [InlineData("motion", "位相を動にする。")]
    [InlineData("none", "位相をなしにする。")]
    public void SetPhase_Self(string name, string expected) =>
        Assert.Equal(expected, Format(new CardEffect("setPhase", EffectScope.Self, null, 0, Name: name)));

    [Fact]
    public void SetPhase_SingleEnemy() =>
        Assert.Equal("敵単体の位相をなしにする。",
            Format(new CardEffect("setPhase", EffectScope.Single, EffectSide.Enemy, 0, Name: "none")));

    [Fact]
    public void InvertPhase_Self() =>
        Assert.Equal("位相を反転する。", Format(new CardEffect("invertPhase", EffectScope.Self, null, 0)));

    [Fact]
    public void AugmentCards_ChooseOneAttack() =>
        Assert.Equal("手札のアタック[N:1]枚を選び、[K:bipolar]を[N:1]付与する。",
            Format(new CardEffect("augmentCards", EffectScope.Self, null, 1, Name: "bipolar",
                Pile: "hand", Select: "choose", CardCount: 1, CardTypeFilter: "Attack")));

    [Fact]
    public void AugmentCards_AllAttacks() =>
        Assert.Equal("手札のアタック全てに[K:bipolar]を[N:1]付与する。",
            Format(new CardEffect("augmentCards", EffectScope.Self, null, 1, Name: "bipolar",
                Pile: "hand", Select: "all", CardTypeFilter: "Attack")));
}
