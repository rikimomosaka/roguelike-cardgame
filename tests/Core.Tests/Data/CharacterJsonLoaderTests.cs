using RoguelikeCardGame.Core.Data;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Data;

public class CharacterJsonLoaderTests
{
    [Fact]
    public void Parse_characterClass_missing_defaults_to_striker()
    {
        var json = """
        {"id":"c","name":"c","maxHp":50,"startingGold":0,"potionSlotCount":3,
         "deck":["strike"]}
        """;

        Assert.Equal(CharacterClass.Striker, CharacterJsonLoader.Parse(json).CharacterClass);
    }

    [Theory]
    [InlineData("Striker", CharacterClass.Striker)]
    [InlineData("Guardian", CharacterClass.Guardian)]
    [InlineData("Tactician", CharacterClass.Tactician)]
    [InlineData("Enhancer", CharacterClass.Enhancer)]
    public void Parse_reads_characterClass(string raw, CharacterClass expected)
    {
        var json = $$"""
        {"id":"c","name":"c","maxHp":50,"startingGold":0,"potionSlotCount":3,
         "deck":["strike"],"characterClass":"{{raw}}"}
        """;

        Assert.Equal(expected, CharacterJsonLoader.Parse(json).CharacterClass);
    }

    [Theory]
    [InlineData("Universal")]
    [InlineData("striker")]
    [InlineData("Unknown")]
    public void Parse_rejects_unknown_characterClass(string raw)
    {
        var json = $$"""
        {"id":"c","name":"c","maxHp":50,"startingGold":0,"potionSlotCount":3,
         "deck":["strike"],"characterClass":"{{raw}}"}
        """;

        Assert.Throws<CharacterJsonException>(() => CharacterJsonLoader.Parse(json));
    }

    [Fact]
    public void Parse_heightTier_missing_defaults_to_5_character()
    {
        var json = """
        {"id":"c","name":"c","maxHp":50,"startingGold":0,"potionSlotCount":3,
         "deck":["strike"]}
        """;
        var def = CharacterJsonLoader.Parse(json);
        Assert.Equal(5, def.HeightTier);
    }

    [Fact]
    public void Parse_heightTier_value_is_preserved_character()
    {
        var json = """
        {"id":"c","name":"c","maxHp":50,"startingGold":0,"potionSlotCount":3,
         "deck":["strike"],"heightTier":4}
        """;
        var def = CharacterJsonLoader.Parse(json);
        Assert.Equal(4, def.HeightTier);
    }

    [Fact]
    public void Parse_heightTier_below_range_throws_character()
    {
        var json = """
        {"id":"c","name":"c","maxHp":50,"startingGold":0,"potionSlotCount":3,
         "deck":["strike"],"heightTier":0}
        """;
        Assert.Throws<CharacterJsonException>(() => CharacterJsonLoader.Parse(json));
    }

    [Fact]
    public void Parse_heightTier_above_range_throws_character()
    {
        var json = """
        {"id":"c","name":"c","maxHp":50,"startingGold":0,"potionSlotCount":3,
         "deck":["strike"],"heightTier":11}
        """;
        Assert.Throws<CharacterJsonException>(() => CharacterJsonLoader.Parse(json));
    }

    [Fact]
    public void Parse_heightTier_non_number_throws_character()
    {
        var json = """
        {"id":"c","name":"c","maxHp":50,"startingGold":0,"potionSlotCount":3,
         "deck":["strike"],"heightTier":"x"}
        """;
        Assert.Throws<CharacterJsonException>(() => CharacterJsonLoader.Parse(json));
    }

    [Fact]
    public void Parse_skills_reads_resource_cost_once_per_turn_and_card_effects()
    {
        var json = """
        {"id":"c","name":"c","maxHp":50,"startingGold":0,"potionSlotCount":3,
         "deck":["strike"],"skills":[
           {"id":"tactical","name":"技","comboGaugeCost":4,"energyCost":1,
           "oncePerTurn":true,"description":"説明",
            "usesPerTurn":2,
            "effects":[{"action":"buff","scope":"self","amount":2,"name":"strength"}]}
         ]}
        """;

        var def = CharacterJsonLoader.Parse(json);
        var skill = Assert.Single(def.EffectiveSkills);
        Assert.Equal("tactical", skill.Id);
        Assert.Equal(1, skill.Cost.Energy);
        Assert.Equal(4, skill.Cost.ComboGauge);
        Assert.True(skill.OncePerTurn);
        Assert.Equal(2, skill.EffectiveUsesPerTurn);
        Assert.Equal("buff", Assert.Single(skill.Effects).Action);
    }

    [Fact]
    public void Parse_reads_character_description_and_trait_description()
    {
        var json = """
        {"id":"c","name":"c","maxHp":50,"startingGold":0,"potionSlotCount":3,
         "deck":["strike"],"description":"人物説明","traitDescription":"特性説明"}
        """;

        var def = CharacterJsonLoader.Parse(json);

        Assert.Equal("人物説明", def.Description);
        Assert.Equal("特性説明", def.TraitDescription);
    }
}
