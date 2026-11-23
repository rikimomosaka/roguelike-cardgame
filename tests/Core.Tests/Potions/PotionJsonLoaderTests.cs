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

    [Fact]
    public void ParsePotionReadsEntityLevelUsageLimit()
    {
        var json = """
        {
          "id": "limited_potion",
          "name": "制限ポーション",
          "rarity": 1,
          "usesPerTurn": 2,
          "effects": [ { "action": "heal", "scope": "self", "amount": 1 } ]
        }
        """;

        var def = PotionJsonLoader.Parse(json);

        Assert.Equal(2, def.UsesPerTurn);
    }

    [Fact]
    public void ParsePotionRejectsEffectLevelUsageLimit()
    {
        var json = """
        {
          "id": "bad_potion",
          "name": "誤設定",
          "rarity": 1,
          "effects": [ { "action": "heal", "scope": "self", "amount": 1, "usesPerTurn": 1 } ]
        }
        """;

        var error = Assert.Throws<PotionJsonException>(() => PotionJsonLoader.Parse(json));
        Assert.Contains("カード・ポーション・キャラクタースキル本体", error.Message);
    }
}
