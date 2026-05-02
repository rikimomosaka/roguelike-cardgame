using System.Linq;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Relics;
using RoguelikeCardGame.Core.Tests.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Relics;

/// <summary>
/// Phase 10.5.L1.5: relic-level Trigger 廃止に伴い、JSON loader テストを per-effect
/// trigger 形式に書き換えた。top-level "trigger" は読み捨てられ、各 effects[].trigger が
/// effect 自身の Trigger 文字列になる。
/// </summary>
public class RelicJsonLoaderTests
{
    [Fact]
    public void ParseBurningBlood_LoadsEffects()
    {
        var def = RelicJsonLoader.Parse(JsonFixtures.BurningBloodJson);
        Assert.Equal("burning_blood", def.Id);
        Assert.Single(def.Effects);
    }

    [Fact]
    public void ParseLantern_EmptyEffects()
    {
        var def = RelicJsonLoader.Parse(JsonFixtures.LanternJson);
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
    public void TopLevel_trigger_isIgnored_silently()
    {
        // Phase 10.5.L1.5: 旧 JSON が top-level "trigger" を持っていても loader は無視する
        var json = """
        {"id":"r","name":"n","rarity":1,"trigger":"OnMidnight","effects":[]}
        """;
        var def = RelicJsonLoader.Parse(json);
        Assert.Equal("r", def.Id);
        Assert.Empty(def.Effects);
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
    public void ParseRelicWithEffectLevel_TriggerField()
    {
        // Phase 10.5.L1.5: 各 effect が trigger 文字列を持つ形式
        var json = """
        {
          "id":"r1","name":"r1","rarity":1,
          "effects":[
            { "action":"block","scope":"self","amount":5,"trigger":"OnTurnStart" }
          ]
        }
        """;
        var def = RelicJsonLoader.Parse(json);
        Assert.Single(def.Effects);
        Assert.Equal("OnTurnStart", def.Effects[0].Trigger);
    }

    [Fact]
    public void ParseRelicWithMultipleEffects_DifferentTriggers()
    {
        var json = """
        {
          "id":"r2","name":"r2","rarity":1,
          "effects":[
            { "action":"gainMaxHp","scope":"self","amount":8,"trigger":"OnPickup" },
            { "action":"block","scope":"self","amount":5,"trigger":"OnBattleStart" }
          ]
        }
        """;
        var def = RelicJsonLoader.Parse(json);
        Assert.Equal(2, def.Effects.Count);
        Assert.Equal("OnPickup", def.Effects[0].Trigger);
        Assert.Equal("OnBattleStart", def.Effects[1].Trigger);
    }

    [Fact]
    public void Implemented_defaults_to_true_when_field_missing()
    {
        var json = """
        {"id":"r","name":"n","rarity":1,"effects":[]}
        """;
        var def = RelicJsonLoader.Parse(json);
        Assert.True(def.Implemented);
    }

    [Fact]
    public void Implemented_explicit_false_is_loaded()
    {
        var json = """
        {"id":"r","name":"n","rarity":1,"effects":[],"implemented":false}
        """;
        var def = RelicJsonLoader.Parse(json);
        Assert.False(def.Implemented);
    }

    [Fact]
    public void Implemented_explicit_true_is_loaded()
    {
        var json = """
        {"id":"r","name":"n","rarity":1,"effects":[],"implemented":true}
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
                "description": "迷っても、心を留めるための小さな錨。",
                "effects": [
                  { "action": "gainMaxHp", "scope": "self", "amount": 8, "trigger": "OnPickup" }
                ],
                "implemented": true
              }
            }
          ]
        }
        """;
        var def = RelicJsonLoader.Parse(json);
        Assert.Equal("anchor", def.Id);
        Assert.Equal("アンカー", def.Name);
        Assert.Equal("迷っても、心を留めるための小さな錨。", def.Description);
        Assert.Single(def.Effects);
        Assert.Equal("OnPickup", def.Effects[0].Trigger);
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
                "effects": [
                  { "action": "gainMaxHp", "scope": "self", "amount": 8, "trigger": "OnPickup" }
                ]
              }
            },
            {
              "version": "v2",
              "spec": {
                "rarity": 2,
                "effects": [
                  { "action": "gainMaxHp", "scope": "self", "amount": 16, "trigger": "OnPickup" }
                ]
              }
            }
          ]
        }
        """;
        var def = RelicJsonLoader.Parse(json);
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
            { "version": "v1", "spec": { "rarity": 1, "effects": [] } }
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
            { "version": "v1", "spec": { "rarity": 1, "effects": [] } }
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
        Assert.Single(def.Effects);
    }
}
