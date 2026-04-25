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
        var eff = def.Effects.Single();
        Assert.Equal("block", eff.Action);
        Assert.Equal(12, eff.Amount);
        Assert.True(eff.BattleOnly);
        Assert.False(def.IsUsableOutsideBattle);
    }

    [Fact]
    public void ParseFirePotion()
    {
        var def = PotionJsonLoader.Parse(JsonFixtures.FirePotionJson);
        var eff = def.Effects.Single();
        Assert.Equal("attack", eff.Action);
        Assert.Equal(20, eff.Amount);
        Assert.True(eff.BattleOnly);
        Assert.False(def.IsUsableOutsideBattle);
    }

    [Fact]
    public void ParsePotionWithoutBattleOnly_IsUsableOutsideBattle()
    {
        var json = """
        {
          "id": "health_test",
          "name": "テスト",
          "rarity": 1,
          "effects": [ { "action": "heal", "scope": "self", "amount": 15 } ]
        }
        """;
        var def = PotionJsonLoader.Parse(json);
        var eff = def.Effects.Single();
        Assert.False(eff.BattleOnly);
        Assert.True(def.IsUsableOutsideBattle);
    }
}
