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
      "tiers": [1, 2, 3],
      "rarity": "common",
      "startMessage": "開始メッセージ",
      "choices": [
        {
          "label": "Gold を貰う",
          "effects": [ { "type": "gainGold", "amount": 30 } ],
          "resultMessage": "Gold を受け取った"
        },
        {
          "label": "HP を失って Gold を大量に貰う",
          "condition": { "type": "minHp", "amount": 10 },
          "effects": [
            { "type": "takeDamage", "amount": 5 },
            { "type": "gainGold", "amount": 100 }
          ],
          "resultMessage": "痛みと引き換えに金貨を得た"
        },
        {
          "label": "レリック",
          "condition": { "type": "minGold", "amount": 50 },
          "effects": [ { "type": "payGold", "amount": 50 }, { "type": "gainRelicRandom", "rarity": 1 } ],
          "resultMessage": "怪しい小物を受け取った"
        },
        {
          "label": "カード報酬",
          "effects": [ { "type": "grantCardReward" } ],
          "resultMessage": "新たな技を閃いた"
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
        Assert.Equal("開始メッセージ", def.StartMessage);
        Assert.Equal(new[] { 1, 2, 3 }, def.Tiers);
        Assert.Equal(EventRarity.Common, def.Rarity);
        Assert.Null(def.Condition);
        Assert.Equal(4, def.Choices.Length);

        Assert.Null(def.Choices[0].Condition);
        Assert.IsType<EventEffect.GainGold>(def.Choices[0].Effects[0]);
        Assert.Equal("Gold を受け取った", def.Choices[0].ResultMessage);

        Assert.IsType<EventCondition.MinHp>(def.Choices[1].Condition);
        Assert.IsType<EventEffect.TakeDamage>(def.Choices[1].Effects[0]);
        Assert.Equal("痛みと引き換えに金貨を得た", def.Choices[1].ResultMessage);

        Assert.IsType<EventCondition.MinGold>(def.Choices[2].Condition);
        var gr = Assert.IsType<EventEffect.GainRelicRandom>(def.Choices[2].Effects[1]);
        Assert.Equal(CardRarity.Common, gr.Rarity);

        Assert.IsType<EventEffect.GrantCardReward>(def.Choices[3].Effects[0]);
    }

    [Fact]
    public void Parse_WithEventLevelCondition_ReadsIt()
    {
        const string json = """
        {
          "id": "e1", "name": "n", "tiers": [2], "rarity": "rare",
          "startMessage": "s",
          "condition": { "type": "minGold", "amount": 100 },
          "choices": [ { "label": "a", "effects": [], "resultMessage": "r" } ]
        }
        """;
        var def = EventJsonLoader.Parse(json);
        Assert.IsType<EventCondition.MinGold>(def.Condition);
        Assert.Equal(EventRarity.Rare, def.Rarity);
        Assert.Equal(new[] { 2 }, def.Tiers);
    }

    [Fact]
    public void Parse_EmptyTiers_IsAllowed()
    {
        const string json = """
        {
          "id": "e2", "name": "n", "tiers": [], "rarity": "common",
          "startMessage": "s",
          "choices": [ { "label": "a", "effects": [], "resultMessage": "r" } ]
        }
        """;
        var def = EventJsonLoader.Parse(json);
        Assert.Empty(def.Tiers);
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
        { "id": "x", "name": "n", "tiers": [1], "rarity": "common", "startMessage": "s", "choices": [ { "label": "a", "effects": [ { "type": "nope" } ], "resultMessage": "r" } ] }
        """;
        Assert.Throws<EventJsonException>(() => EventJsonLoader.Parse(bad));
    }

    [Fact]
    public void Parse_MissingTiers_Throws()
    {
        const string bad = """
        { "id": "x", "name": "n", "rarity": "common", "startMessage": "s", "choices": [ { "label": "a", "effects": [], "resultMessage": "r" } ] }
        """;
        Assert.Throws<EventJsonException>(() => EventJsonLoader.Parse(bad));
    }

    [Fact]
    public void Parse_InvalidRarity_Throws()
    {
        const string bad = """
        { "id": "x", "name": "n", "tiers": [1], "rarity": "legendary", "startMessage": "s", "choices": [ { "label": "a", "effects": [], "resultMessage": "r" } ] }
        """;
        Assert.Throws<EventJsonException>(() => EventJsonLoader.Parse(bad));
    }
}
