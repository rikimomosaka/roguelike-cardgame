using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Events;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Events;

public class EventJsonLoaderTests
{
    private const string SampleJson = """
    {
      "id": "test_event",
      "name": "テストイベント",
      "description": "説明文",
      "choices": [
        {
          "label": "Gold を貰う",
          "effects": [ { "type": "gainGold", "amount": 30 } ]
        },
        {
          "label": "HP を失って Gold を大量に貰う",
          "condition": { "type": "minHp", "amount": 10 },
          "effects": [
            { "type": "takeDamage", "amount": 5 },
            { "type": "gainGold", "amount": 100 }
          ]
        },
        {
          "label": "レリック",
          "condition": { "type": "minGold", "amount": 50 },
          "effects": [ { "type": "payGold", "amount": 50 }, { "type": "gainRelicRandom", "rarity": 1 } ]
        },
        {
          "label": "カード報酬",
          "effects": [ { "type": "grantCardReward" } ]
        }
      ]
    }
    """;

    [Fact]
    public void Parse_ValidJson_ReturnsDefinition()
    {
        var def = EventJsonLoader.Parse(SampleJson);
        Assert.Equal("test_event", def.Id);
        Assert.Equal("テストイベント", def.Name);
        Assert.Equal(4, def.Choices.Length);

        Assert.Null(def.Choices[0].Condition);
        Assert.IsType<EventEffect.GainGold>(def.Choices[0].Effects[0]);

        Assert.IsType<EventCondition.MinHp>(def.Choices[1].Condition);
        Assert.IsType<EventEffect.TakeDamage>(def.Choices[1].Effects[0]);

        Assert.IsType<EventCondition.MinGold>(def.Choices[2].Condition);
        var gr = Assert.IsType<EventEffect.GainRelicRandom>(def.Choices[2].Effects[1]);
        Assert.Equal(CardRarity.Common, gr.Rarity);

        Assert.IsType<EventEffect.GrantCardReward>(def.Choices[3].Effects[0]);
    }

    [Fact]
    public void Parse_InvalidJson_Throws()
    {
        Assert.Throws<EventJsonException>(() => EventJsonLoader.Parse("{"));
    }

    [Fact]
    public void Parse_UnknownEffectType_Throws()
    {
        const string bad = """
        { "id": "x", "name": "n", "description": "d", "choices": [ { "label": "a", "effects": [ { "type": "nope" } ] } ] }
        """;
        Assert.Throws<EventJsonException>(() => EventJsonLoader.Parse(bad));
    }
}
