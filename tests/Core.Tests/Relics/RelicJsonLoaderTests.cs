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

    [Fact]
    public void Implemented_defaults_to_true_when_field_missing()
    {
        var json = """
        {"id":"r","name":"n","rarity":1,"trigger":"OnPickup","effects":[]}
        """;
        var def = RelicJsonLoader.Parse(json);
        Assert.True(def.Implemented);
    }

    [Fact]
    public void Implemented_explicit_false_is_loaded()
    {
        var json = """
        {"id":"r","name":"n","rarity":1,"trigger":"OnPickup","effects":[],"implemented":false}
        """;
        var def = RelicJsonLoader.Parse(json);
        Assert.False(def.Implemented);
    }

    [Fact]
    public void Implemented_explicit_true_is_loaded()
    {
        var json = """
        {"id":"r","name":"n","rarity":1,"trigger":"OnPickup","effects":[],"implemented":true}
        """;
        var def = RelicJsonLoader.Parse(json);
        Assert.True(def.Implemented);
    }

    // ---- Phase 10.5.L1: versioned 形式 ----

    [Fact]
    public void ParseVersioned_singleVersion_resolvesActive()
    {
        var json = """
        {
          "id": "anchor",
          "name": "アンカー",
          "displayName": null,
          "activeVersion": "v1",
          "versions": [
            {
              "version": "v1",
              "createdAt": "2026-05-02T00:00:00Z",
              "label": "original",
              "spec": {
                "rarity": 1,
                "trigger": "OnPickup",
                "description": "迷っても、心を留めるための小さな錨。",
                "effects": [{ "action": "gainMaxHp", "scope": "self", "amount": 8 }],
                "implemented": true
              }
            }
          ]
        }
        """;
        var def = RelicJsonLoader.Parse(json);
        Assert.Equal("anchor", def.Id);
        Assert.Equal("アンカー", def.Name);
        Assert.Equal(RelicTrigger.OnPickup, def.Trigger);
        Assert.Equal("迷っても、心を留めるための小さな錨。", def.Description);
        Assert.Single(def.Effects);
        Assert.True(def.Implemented);
    }

    [Fact]
    public void ParseVersioned_multipleVersions_picksActive()
    {
        var json = """
        {
          "id": "anchor",
          "name": "アンカー",
          "activeVersion": "v2",
          "versions": [
            {
              "version": "v1",
              "spec": {
                "rarity": 1,
                "trigger": "OnPickup",
                "effects": [{ "action": "gainMaxHp", "scope": "self", "amount": 8 }]
              }
            },
            {
              "version": "v2",
              "spec": {
                "rarity": 2,
                "trigger": "Passive",
                "effects": [{ "action": "gainMaxHp", "scope": "self", "amount": 16 }]
              }
            }
          ]
        }
        """;
        var def = RelicJsonLoader.Parse(json);
        Assert.Equal(RelicTrigger.Passive, def.Trigger);
        Assert.Equal(CardRarity.Rare, def.Rarity);
        Assert.Equal(16, def.Effects[0].Amount);
    }

    [Fact]
    public void ParseVersioned_unknownActiveVersion_throws()
    {
        var json = """
        {
          "id": "x",
          "name": "x",
          "activeVersion": "v9",
          "versions": [
            { "version": "v1", "spec": { "rarity": 1, "trigger": "OnPickup", "effects": [] } }
          ]
        }
        """;
        var ex = Assert.Throws<RelicJsonException>(() => RelicJsonLoader.Parse(json));
        Assert.Contains("v9", ex.Message);
    }

    [Fact]
    public void ParseVersioned_missingActiveVersion_throws()
    {
        var json = """
        {
          "id": "x",
          "name": "x",
          "versions": [
            { "version": "v1", "spec": { "rarity": 1, "trigger": "OnPickup", "effects": [] } }
          ]
        }
        """;
        Assert.Throws<RelicJsonException>(() => RelicJsonLoader.Parse(json));
    }

    [Fact]
    public void ParseFlat_stillWorks_backwardCompat()
    {
        // flat (legacy) も依然として動くこと。
        var def = RelicJsonLoader.Parse(JsonFixtures.BurningBloodJson);
        Assert.Equal("burning_blood", def.Id);
        Assert.Equal(RelicTrigger.OnBattleEnd, def.Trigger);
    }
}
