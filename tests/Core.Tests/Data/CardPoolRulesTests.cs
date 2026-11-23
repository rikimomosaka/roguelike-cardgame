using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Data;

public sealed class CardPoolRulesTests
{
    private static readonly CharacterDefinition Striker =
        new("striker_a", "Striker", 80, 0, 3, new[] { "strike" });

    [Fact]
    public void Class_reward_requires_reward_kind_matching_class_and_eligibility()
    {
        var card = Card(PoolClass: CardPoolClass.Striker,
            Kinds: new[] { CardPoolKind.Basic, CardPoolKind.Reward });

        Assert.True(CardPoolRules.IsClassRewardFor(card, Striker));
        Assert.False(CardPoolRules.IsUniversalRewardFor(card, Striker));
        Assert.True(CardPoolRules.IsBasic(card));
    }

    [Fact]
    public void Character_restriction_excludes_other_character_of_the_same_class()
    {
        var restricted = Card(PoolClass: CardPoolClass.Striker,
            Kinds: new[] { CardPoolKind.Reward }, Eligible: new[] { "striker_b" });

        Assert.False(CardPoolRules.IsClassRewardFor(restricted, Striker));
    }

    [Fact]
    public void Universal_reward_is_separate_from_class_reward()
    {
        var card = Card(PoolClass: CardPoolClass.Universal,
            Kinds: new[] { CardPoolKind.Reward });

        Assert.True(CardPoolRules.IsUniversalRewardFor(card, Striker));
        Assert.False(CardPoolRules.IsClassRewardFor(card, Striker));
    }

    private static CardDefinition Card(
        CardPoolClass PoolClass,
        CardPoolKind[] Kinds,
        string[]? Eligible = null) =>
        new("card", "Card", null, CardRarity.Common, CardType.Skill, 1, null,
            System.Array.Empty<CardEffect>(), null, null,
            PoolClass: PoolClass, PoolKinds: Kinds, EligibleCharacterIds: Eligible);
}
