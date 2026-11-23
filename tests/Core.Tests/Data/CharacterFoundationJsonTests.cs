using RoguelikeCardGame.Core.Data;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Data;

public sealed class CharacterFoundationJsonTests
{
    [Fact]
    public void Parse_reads_turn_resources_and_data_driven_trait()
    {
        var json = """
        {"id":"c","name":"c","maxHp":50,"startingGold":0,"potionSlotCount":3,
         "deck":["strike"],"energyPerTurn":4,"drawPerTurn":6,
         "trait":{"id":"combo_flash","name":"Combo Flash",
          "trigger":"OnComboReached","threshold":4,"usesPerTurn":1,
          "description":"Trigger once after reaching four combo.",
          "effects":[{"action":"buff","scope":"self","amount":1,"name":"afterimage"}]}}
        """;

        var def = CharacterJsonLoader.Parse(json);

        Assert.Equal(4, def.EnergyPerTurn);
        Assert.Equal(6, def.DrawPerTurn);
        var trait = Assert.IsType<CharacterTraitDefinition>(def.Trait);
        Assert.Equal("combo_flash", trait.Id);
        Assert.Equal(CharacterTraitTrigger.OnComboReached, trait.Trigger);
        Assert.Equal(4, trait.Threshold);
        Assert.Equal(1, trait.UsesPerTurn);
        Assert.True(trait.HasPerTurnLimit);
        Assert.Equal("afterimage", Assert.Single(trait.Effects).Name);
    }

    [Fact]
    public void Parse_missing_turn_resources_use_current_solo_defaults()
    {
        var json = """
        {"id":"c","name":"c","maxHp":50,"startingGold":0,"potionSlotCount":3,
         "deck":["strike"]}
        """;

        var def = CharacterJsonLoader.Parse(json);

        Assert.Equal(3, def.EnergyPerTurn);
        Assert.Equal(5, def.DrawPerTurn);
        Assert.Null(def.Trait);
    }

    [Theory]
    [InlineData("energyPerTurn")]
    [InlineData("drawPerTurn")]
    public void Parse_rejects_negative_turn_resource(string property)
    {
        var json = $$"""
        {"id":"c","name":"c","maxHp":50,"startingGold":0,"potionSlotCount":3,
         "deck":["strike"],"{{property}}":-1}
        """;

        Assert.Throws<CharacterJsonException>(() => CharacterJsonLoader.Parse(json));
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("onTurnStart")]
    public void Parse_rejects_unknown_trait_trigger(string trigger)
    {
        var json = """
        {"id":"c","name":"c","maxHp":50,"startingGold":0,"potionSlotCount":3,
         "deck":["strike"],
         "trait":{"id":"t","name":"t","trigger":"$TRIGGER",
          "effects":[{"action":"block","scope":"self","amount":1}]}}
        """.Replace("$TRIGGER", trigger, System.StringComparison.Ordinal);

        Assert.Throws<CharacterJsonException>(() => CharacterJsonLoader.Parse(json));
    }

    [Fact]
    public void Parse_rejects_trait_without_effects()
    {
        var json = """
        {"id":"c","name":"c","maxHp":50,"startingGold":0,"potionSlotCount":3,
         "deck":["strike"],
         "trait":{"id":"t","name":"t","trigger":"OnTurnStart","effects":[]}}
        """;

        Assert.Throws<CharacterJsonException>(() => CharacterJsonLoader.Parse(json));
    }
}
