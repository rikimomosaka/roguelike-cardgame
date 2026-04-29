using RoguelikeCardGame.Core.Data;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Data;

public class CharacterJsonLoaderTests
{
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
        Assert.ThrowsAny<System.Exception>(() => CharacterJsonLoader.Parse(json));
    }

    [Fact]
    public void Parse_heightTier_above_range_throws_character()
    {
        var json = """
        {"id":"c","name":"c","maxHp":50,"startingGold":0,"potionSlotCount":3,
         "deck":["strike"],"heightTier":11}
        """;
        Assert.ThrowsAny<System.Exception>(() => CharacterJsonLoader.Parse(json));
    }
}
