using System.Linq;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Tests.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Cards;

public class CardJsonLoaderTests
{
    [Fact]
    public void ParseStrike_FillsAllFields()
    {
        var def = CardJsonLoader.Parse(JsonFixtures.StrikeJson);

        Assert.Equal("strike", def.Id);
        Assert.Equal("ストライク", def.Name);
        Assert.Null(def.DisplayName);
        Assert.Equal(CardRarity.Common, def.Rarity);
        Assert.Equal(CardType.Attack, def.CardType);
        Assert.Equal(1, def.Cost);

        var dmg = def.Effects.Single();
        Assert.Equal("attack", dmg.Action);
        Assert.Equal(6, dmg.Amount);

        Assert.NotNull(def.UpgradedEffects);
        var upDmg = def.UpgradedEffects!.Single();
        Assert.Equal("attack", upDmg.Action);
        Assert.Equal(9, upDmg.Amount);
    }

    [Fact]
    public void ParseDefend_ParsesGainBlock()
    {
        var def = CardJsonLoader.Parse(JsonFixtures.DefendJson);
        var eff = def.Effects.Single();
        Assert.Equal("block", eff.Action);
        Assert.Equal(5, eff.Amount);
    }

    [Fact]
    public void ParseDisplayName_WhenProvided()
    {
        var def = CardJsonLoader.Parse(JsonFixtures.StrikePromoJson);
        Assert.Equal("ストライク(周年記念)", def.DisplayName);
        Assert.Equal(CardRarity.Promo, def.Rarity);
        Assert.Null(def.UpgradedEffects);
    }

    [Fact]
    public void ParseUnplayableCard_CostIsNull()
    {
        var def = CardJsonLoader.Parse(JsonFixtures.UnplayableCurseJson);
        Assert.Null(def.Cost);
        Assert.Empty(def.Effects);
    }

    [Fact]
    public void Parse_with_arbitrary_action_string_succeeds()
    {
        var def = CardJsonLoader.Parse(JsonFixtures.UnknownEffectJson);
        var eff = def.Effects.Single();
        Assert.Equal("summonUnit", eff.Action);
    }

    [Fact]
    public void BrokenJson_ThrowsCardJsonException()
    {
        Assert.Throws<CardJsonException>(() => CardJsonLoader.Parse(JsonFixtures.BrokenJson));
    }

    // --- 新規テスト ---

    [Fact]
    public void MissingId_Throws()
    {
        var ex = Assert.Throws<CardJsonException>(() => CardJsonLoader.Parse(JsonFixtures.MissingIdJson));
        Assert.Contains("id", ex.Message);
    }

    [Fact]
    public void MissingName_Throws()
    {
        var ex = Assert.Throws<CardJsonException>(() => CardJsonLoader.Parse(JsonFixtures.MissingNameJson));
        Assert.Contains("name", ex.Message);
    }

    [Fact]
    public void UnknownCardType_Throws()
    {
        var ex = Assert.Throws<CardJsonException>(() => CardJsonLoader.Parse(JsonFixtures.UnknownCardTypeJson));
        // メッセージに "Creature" または "cardType" が含まれること
        Assert.True(
            ex.Message.Contains("Creature") || ex.Message.Contains("cardType"),
            $"Expected message to contain 'Creature' or 'cardType', but was: {ex.Message}");
    }

    [Fact]
    public void RarityOutOfRange_Throws()
    {
        var ex = Assert.Throws<CardJsonException>(() => CardJsonLoader.Parse(JsonFixtures.RarityOutOfRangeJson));
        // メッセージに "rarity" または "99" が含まれること
        Assert.True(
            ex.Message.Contains("rarity") || ex.Message.Contains("99"),
            $"Expected message to contain 'rarity' or '99', but was: {ex.Message}");
    }

    [Fact]
    public void UpgradedEffectsWrongType_Throws()
    {
        Assert.Throws<CardJsonException>(() => CardJsonLoader.Parse(JsonFixtures.UpgradedEffectsWrongTypeJson));
    }

    [Fact]
    public void UpgradedEffectsExplicitlyNull_ReturnsNull()
    {
        var def = CardJsonLoader.Parse(JsonFixtures.UpgradedEffectsExplicitNullJson);
        Assert.Null(def.UpgradedEffects);
    }

    [Fact]
    public void Parse_with_upgradedCost_only_sets_field()
    {
        var json = """
        {
          "id":"hb","name":"重撃","rarity":1,"cardType":"Attack",
          "cost":2,"upgradedCost":1,
          "effects":[{"action":"attack","scope":"single","side":"enemy","amount":12}]
        }
        """;
        var def = CardJsonLoader.Parse(json);
        Assert.Equal(2, def.Cost);
        Assert.Equal(1, def.UpgradedCost);
        Assert.Null(def.UpgradedEffects);
        Assert.True(def.IsUpgradable);
    }

    [Fact]
    public void Parse_without_upgradedCost_or_upgradedEffects_yields_non_upgradable()
    {
        var json = """
        {
          "id":"c","name":"呪い","rarity":1,"cardType":"Curse",
          "cost":null,
          "effects":[]
        }
        """;
        var def = CardJsonLoader.Parse(json);
        Assert.False(def.IsUpgradable);
        Assert.Null(def.UpgradedCost);
        Assert.Null(def.UpgradedEffects);
    }

    [Fact]
    public void Parse_with_keywords_array()
    {
        var json = """
        {
          "id":"w","name":"Wild Strike","rarity":2,"cardType":"Attack",
          "cost":5,
          "keywords":["wild"],
          "effects":[{"action":"attack","scope":"single","side":"enemy","amount":12}]
        }
        """;
        var def = CardJsonLoader.Parse(json);
        Assert.NotNull(def.Keywords);
        Assert.Contains("wild", def.Keywords);
    }

    [Fact]
    public void Parse_with_status_card_type()
    {
        var json = """
        {
          "id":"s","name":"傷","rarity":1,"cardType":"Status",
          "cost":null,
          "effects":[]
        }
        """;
        var def = CardJsonLoader.Parse(json);
        Assert.Equal(CardType.Status, def.CardType);
    }

    [Fact]
    public void Parse_with_curse_card_type()
    {
        var json = """
        {
          "id":"c","name":"呪い","rarity":1,"cardType":"Curse",
          "cost":null,
          "effects":[]
        }
        """;
        var def = CardJsonLoader.Parse(json);
        Assert.Equal(CardType.Curse, def.CardType);
    }

    [Fact]
    public void Loads_card_with_description_override()
    {
        var json = """
        {
          "id": "test",
          "name": "テスト",
          "rarity": 0,
          "cardType": "Attack",
          "cost": 1,
          "effects": [{"action":"attack","scope":"single","side":"enemy","amount":6}],
          "description": "手書きの説明文。",
          "upgradedDescription": "強化版の説明文。"
        }
        """;
        var def = CardJsonLoader.Parse(json);
        Assert.Equal("手書きの説明文。", def.Description);
        Assert.Equal("強化版の説明文。", def.UpgradedDescription);
    }

    [Fact]
    public void Loads_card_without_description_keys_yields_null_descriptions()
    {
        var json = """
        {
          "id": "test",
          "name": "テスト",
          "rarity": 0,
          "cardType": "Attack",
          "cost": 1,
          "effects": [{"action":"attack","scope":"single","side":"enemy","amount":6}]
        }
        """;
        var def = CardJsonLoader.Parse(json);
        Assert.Null(def.Description);
        Assert.Null(def.UpgradedDescription);
    }

    [Fact]
    public void Loads_token_rarity()
    {
        var json = """
        {
          "id": "wound",
          "name": "傷",
          "rarity": 5,
          "cardType": "Status",
          "cost": null,
          "effects": []
        }
        """;
        var def = CardJsonLoader.Parse(json);
        Assert.Equal(CardRarity.Token, def.Rarity);
    }

    [Fact]
    public void Loads_card_with_empty_description_strings_normalizes_to_null()
    {
        var json = """
        {
          "id": "test",
          "name": "テスト",
          "rarity": 0,
          "cardType": "Attack",
          "cost": 1,
          "effects": [{"action":"attack","scope":"single","side":"enemy","amount":6}],
          "description": "",
          "upgradedDescription": ""
        }
        """;
        var def = CardJsonLoader.Parse(json);
        Assert.Null(def.Description);
        Assert.Null(def.UpgradedDescription);
    }
}
