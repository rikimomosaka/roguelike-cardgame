using System.Linq;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Potions;
using RoguelikeCardGame.Core.Tests.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Potions;

public class PotionJsonLoaderTests
{
    [Fact]
    public void ParseBlockPotion()
    {
        var def = PotionJsonLoader.Parse(JsonFixtures.BlockPotionJson);
        Assert.Equal("block_potion", def.Id);
        Assert.True(def.UsableInBattle);
        Assert.False(def.UsableOutOfBattle);
        var eff = Assert.IsType<GainBlockEffect>(def.Effects.Single());
        Assert.Equal(12, eff.Amount);
    }

    [Fact]
    public void ParseFirePotion()
    {
        var def = PotionJsonLoader.Parse(JsonFixtures.FirePotionJson);
        var dmg = Assert.IsType<DamageEffect>(def.Effects.Single());
        Assert.Equal(20, dmg.Amount);
    }

    [Fact]
    public void MissingUsableFlags_Throws()
    {
        var ex = Assert.Throws<PotionJsonException>(() => PotionJsonLoader.Parse(JsonFixtures.PotionMissingUsableFlagsJson));
        Assert.Contains("bad_potion", ex.Message);
        Assert.Contains("usableInBattle", ex.Message);
    }
}
