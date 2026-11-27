using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using RoguelikeCardGame.Core.Battle.State;
using RoguelikeCardGame.Core.Cards;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Battle.State;

public sealed class CardAugmentTests
{
    private static BattleCardInstance Strike() => new("c1", "strike", false, null);

    [Fact]
    public void AddKeyword_Stacks_ForKeywordsWithoutACap()
    {
        // 重ねる仕組み自体は残す（上限の無いキーワードは数が増える）。
        var card = Strike().AddKeyword("wild", 1).AddKeyword("wild", 1);

        Assert.Equal(2, card.KeywordCount("wild"));
        var augment = Assert.Single(card.EffectiveAugments);
        Assert.Equal(new CardAugment(CardAugment.KeywordKind, "wild", 2), augment);
    }

    [Fact]
    public void Bipolar_IsCappedAtOne_EvenWhenAddedRepeatedly()
    {
        var card = Strike().AddKeyword(CardKeywords.Bipolar, 1).AddKeyword(CardKeywords.Bipolar, 2);

        Assert.Equal(1, card.KeywordCount(CardKeywords.Bipolar));
        Assert.Equal(new[] { ("bipolar", 1) }, card.AugmentedKeywords());
        Assert.Equal(1, CardKeywords.MaxCount(CardKeywords.Bipolar));
        Assert.Null(CardKeywords.MaxCount("wild"));
    }

    [Fact]
    public void AddKeyword_RejectsNonPositiveCount() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Strike().AddKeyword(CardKeywords.Bipolar, 0));

    [Fact]
    public void RemoveKeyword_ClearsAll()
    {
        var card = Strike().AddKeyword(CardKeywords.Bipolar, 3).RemoveKeyword(CardKeywords.Bipolar);

        Assert.Equal(0, card.KeywordCount(CardKeywords.Bipolar));
        Assert.Empty(card.EffectiveAugments);
    }

    [Fact]
    public void AugmentedKeywords_KeepsAddOrder()
    {
        var card = Strike().AddKeyword("bipolar", 1).AddKeyword("wild", 2);

        Assert.Equal(new[] { ("bipolar", 1), ("wild", 2) }, card.AugmentedKeywords());
    }

    [Fact]
    public void Equality_IncludesAugments()
    {
        var plain = Strike();
        var augmented = Strike().AddKeyword(CardKeywords.Bipolar, 1);

        Assert.NotEqual(plain, augmented);
        Assert.Equal(augmented, Strike().AddKeyword(CardKeywords.Bipolar, 1));
        Assert.Equal(augmented.GetHashCode(), Strike().AddKeyword(CardKeywords.Bipolar, 1).GetHashCode());
    }

    [Fact]
    public void ConsumeCostChangesOnPlay_KeepsAugments()
    {
        var card = Strike().AddKeyword(CardKeywords.Bipolar, 1).ConsumeCostChangesOnPlay();

        Assert.Equal(1, card.KeywordCount(CardKeywords.Bipolar));
    }

    [Fact]
    public void LegacyCardJson_WithoutAugments_IsEmpty()
    {
        var node = JsonNode.Parse(JsonSerializer.Serialize(Strike().AddKeyword("bipolar", 1)))!.AsObject();
        Assert.True(node.Remove("Augments"));

        var restored = JsonSerializer.Deserialize<BattleCardInstance>(node.ToJsonString())!;

        Assert.Empty(restored.EffectiveAugments);
        Assert.Equal(0, restored.KeywordCount("bipolar"));
    }

    [Fact]
    public void CardKeywords_Bipolar_HasJapaneseNameAndDescription()
    {
        var meta = CardKeywords.Get("bipolar");

        Assert.NotNull(meta);
        Assert.Equal("双極", meta!.Name);
        Assert.Equal(
            "位相が[P:calm]の時、手札にある限り、次のターンに持ち越される。\n位相が[P:motion]の時、発動時にエナジーを+[N:X]する。\nこのカードの発動後、双極が消滅する。",
            meta.Description);
    }
}
