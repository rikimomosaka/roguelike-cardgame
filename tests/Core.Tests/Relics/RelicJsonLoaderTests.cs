using System.Linq;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Tests.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Relics;

public class RelicJsonLoaderTests
{
    [Fact]
    public void ParseBurningBlood()
    {
        var def = RelicJsonLoader.Parse(JsonFixtures.BurningBloodJson);
        Assert.Equal("burning_blood", def.Id);
        Assert.Equal(RelicTrigger.OnBattleEnd, def.Trigger);
        Assert.Single(def.Effects);
    }

    [Fact]
    public void ParseLantern_EmptyEffects()
    {
        var def = RelicJsonLoader.Parse(JsonFixtures.LanternJson);
        Assert.Equal(RelicTrigger.Passive, def.Trigger);
        Assert.Empty(def.Effects);
    }

    [Fact]
    public void RarityOutOfRange_Throws()
    {
        var ex = Assert.Throws<RelicJsonException>(() => RelicJsonLoader.Parse(JsonFixtures.RelicBrokenRarityJson));
        Assert.Contains("rarity", ex.Message);
        Assert.Contains("bad_relic", ex.Message);
    }

    [Fact]
    public void UnknownTrigger_Throws()
    {
        var ex = Assert.Throws<RelicJsonException>(() => RelicJsonLoader.Parse(JsonFixtures.RelicUnknownTriggerJson));
        Assert.Contains("trigger", ex.Message);
    }

    [Fact]
    public void ParseRelicWithDamageEffect_SpecializesAsDamageEffect()
    {
        var def = RelicJsonLoader.Parse(JsonFixtures.RelicWithDamageEffectJson);
        var dmg = def.Effects.Single();
        Assert.Equal("attack", dmg.Action);
        Assert.Equal(7, dmg.Amount);
    }

    [Fact]
    public void ParseRelicWithOnTurnStartTrigger()
    {
        var json = """
        {
          "id":"r1","name":"r1","rarity":1,"trigger":"OnTurnStart","effects":[]
        }
        """;
        var def = RelicJsonLoader.Parse(json);
        Assert.Equal(RelicTrigger.OnTurnStart, def.Trigger);
    }

    [Fact]
    public void ParseRelicWithOnTurnEndTrigger()
    {
        var json = """
        {
          "id":"r2","name":"r2","rarity":1,"trigger":"OnTurnEnd","effects":[]
        }
        """;
        var def = RelicJsonLoader.Parse(json);
        Assert.Equal(RelicTrigger.OnTurnEnd, def.Trigger);
    }

    [Fact]
    public void ParseRelicWithOnCardPlayTrigger()
    {
        var json = """
        {
          "id":"r3","name":"r3","rarity":1,"trigger":"OnCardPlay","effects":[]
        }
        """;
        var def = RelicJsonLoader.Parse(json);
        Assert.Equal(RelicTrigger.OnCardPlay, def.Trigger);
    }

    [Fact]
    public void ParseRelicWithOnEnemyDeathTrigger()
    {
        var json = """
        {
          "id":"r4","name":"r4","rarity":1,"trigger":"OnEnemyDeath","effects":[]
        }
        """;
        var def = RelicJsonLoader.Parse(json);
        Assert.Equal(RelicTrigger.OnEnemyDeath, def.Trigger);
    }
}
