using System.Linq;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Tests.Fixtures;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Cards;

public class CardJsonLoaderTests
{
    [Fact]
    public void Parse_reads_root_card_pool_metadata_for_versioned_card()
    {
        var json = """
        {
          "id":"pool_card","name":"Pool card",
          "poolKinds":["Basic","Reward"],
          "poolClass":"Guardian",
          "eligibleCharacterIds":["guardian_a"],
          "activeVersion":"v1",
          "versions":[{"version":"v1","spec":{
            "rarity":1,"cardType":"Skill","cost":1,"effects":[]
          }}]
        }
        """;

        var def = CardJsonLoader.Parse(json);

        Assert.Equal(new[] { CardPoolKind.Basic, CardPoolKind.Reward }, def.EffectivePoolKinds);
        Assert.Equal(CardPoolClass.Guardian, def.PoolClass);
        Assert.Equal(new[] { "guardian_a" }, def.EffectiveEligibleCharacterIds);
    }

    [Fact]
    public void Parse_missing_card_pool_metadata_means_normal_pool_external()
    {
        var def = CardJsonLoader.Parse(JsonFixtures.StrikeJson);

        Assert.Empty(def.EffectivePoolKinds);
        Assert.Null(def.PoolClass);
        Assert.Empty(def.EffectiveEligibleCharacterIds);
    }

    [Theory]
    [InlineData("\"poolKinds\":\"Reward\"")]
    [InlineData("\"poolKinds\":[\"Reward\",\"Reward\"]")]
    [InlineData("\"poolKinds\":[\"Unknown\"]")]
    [InlineData("\"poolClass\":\"Unknown\"")]
    [InlineData("\"eligibleCharacterIds\":\"hero\"")]
    [InlineData("\"eligibleCharacterIds\":[\"hero\",\"hero\"]")]
    public void Parse_rejects_invalid_card_pool_metadata(string metadata)
    {
        var json = $$"""
        {
          "id":"bad_pool","name":"Bad",{{metadata}},
          "rarity":1,"cardType":"Skill","cost":1,"effects":[]
        }
        """;

        Assert.Throws<CardJsonException>(() => CardJsonLoader.Parse(json));
    }

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
    public void Parse_keeps_normal_and_upgraded_keywords_and_usage_limits_independent()
    {
        var json = """
        {
          "id":"independent","name":"Independent","rarity":2,"cardType":"Skill",
          "cost":1,
          "usesPerTurn":1,
          "upgradedUsesPerTurn":3,
          "keywords":["wild"],
          "upgradedKeywords":["superwild"],
          "effects":[]
        }
        """;

        var def = CardJsonLoader.Parse(json);

        Assert.Equal(new[] { "wild" }, def.EffectiveKeywords(upgraded: false));
        Assert.Equal(new[] { "superwild" }, def.EffectiveKeywords(upgraded: true));
        Assert.Equal(1, def.EffectiveUsesPerTurn(upgraded: false));
        Assert.Equal(3, def.EffectiveUsesPerTurn(upgraded: true));
    }

    [Fact]
    public void Parse_does_not_inherit_normal_keywords_into_upgraded_card()
    {
        var json = """
        {
          "id":"base_only","name":"Base only","rarity":2,"cardType":"Skill",
          "cost":1,"upgradedCost":0,
          "keywords":["wild"],
          "effects":[]
        }
        """;

        var def = CardJsonLoader.Parse(json);

        Assert.Equal(new[] { "wild" }, def.EffectiveKeywords(upgraded: false));
        Assert.Null(def.EffectiveKeywords(upgraded: true));
    }

    [Fact]
    public void Parse_rejects_usage_limit_on_an_individual_effect()
    {
        var json = """
        {
          "id":"bad_limit","name":"Bad","rarity":1,"cardType":"Skill",
          "cost":1,
          "effects":[{"action":"draw","scope":"self","amount":1,"usesPerTurn":1}]
        }
        """;

        var error = Assert.Throws<CardJsonException>(() => CardJsonLoader.Parse(json));
        Assert.Contains("カード・ポーション・キャラクタースキル本体", error.Message);
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

    // --- Phase 10.5.H: versioned schema 対応テスト ---

    [Fact]
    public void Parses_versioned_card_format()
    {
        var json = """
        {
          "id": "strike",
          "name": "ストライク",
          "displayName": null,
          "activeVersion": "v1",
          "versions": [
            {
              "version": "v1",
              "createdAt": "2026-05-01T00:00:00Z",
              "label": "original",
              "spec": {
                "rarity": 1,
                "cardType": "Attack",
                "cost": 1,
                "effects": [{ "action": "attack", "scope": "single", "side": "enemy", "amount": 6 }]
              }
            }
          ]
        }
        """;
        var def = CardJsonLoader.Parse(json);
        Assert.Equal("strike", def.Id);
        Assert.Equal("ストライク", def.Name);
        Assert.Equal(CardRarity.Common, def.Rarity);
        Assert.Equal(CardType.Attack, def.CardType);
        Assert.Equal(1, def.Cost);
        Assert.Single(def.Effects);
    }

    [Fact]
    public void Versioned_with_multiple_versions_picks_active()
    {
        var json = """
        {
          "id": "strike",
          "name": "ストライク",
          "activeVersion": "v2",
          "versions": [
            { "version": "v1", "spec": { "rarity": 1, "cardType": "Attack", "cost": 1, "effects": [{ "action": "attack", "scope": "single", "side": "enemy", "amount": 6 }] } },
            { "version": "v2", "spec": { "rarity": 1, "cardType": "Attack", "cost": 0, "effects": [{ "action": "attack", "scope": "single", "side": "enemy", "amount": 8 }] } }
          ]
        }
        """;
        var def = CardJsonLoader.Parse(json);
        Assert.Equal(0, def.Cost);
        Assert.Equal(8, def.Effects[0].Amount);
    }

    [Fact]
    public void Versioned_unknown_active_version_throws()
    {
        var json = """
        {
          "id": "x", "name": "x",
          "activeVersion": "v99",
          "versions": [{ "version": "v1", "spec": { "rarity": 1, "cardType": "Attack", "cost": 1, "effects": [] } }]
        }
        """;
        Assert.Throws<CardJsonException>(() => CardJsonLoader.Parse(json));
    }

    [Fact]
    public void Flat_format_still_parses_for_backward_compat()
    {
        // 既存 35 card JSON は flat 形式のままなので、これが緑のままであることが必須
        var json = """
        {
          "id": "strike",
          "name": "ストライク",
          "rarity": 1,
          "cardType": "Attack",
          "cost": 1,
          "effects": [{ "action": "attack", "scope": "single", "side": "enemy", "amount": 6 }]
        }
        """;
        var def = CardJsonLoader.Parse(json);
        Assert.Equal("strike", def.Id);
        Assert.Equal(1, def.Cost);
    }

    [Fact]
    public void Parses_grouped_status_changes_without_legacy_amount()
    {
        var json = """
        {
          "id": "grouped", "name": "grouped", "rarity": 1,
          "cardType": "Skill", "cost": 1,
          "effects": [{
            "action": "debuff", "scope": "self",
            "statusChanges": [
              { "name": "silence", "amount": 1 },
              { "name": "handCostIncrease", "amount": 2 }
            ]
          }]
        }
        """;

        var effect = Assert.Single(CardJsonLoader.Parse(json).Effects);

        Assert.Null(effect.Name);
        Assert.Equal(0, effect.Amount);
        Assert.Equal(
            new[] { new StatusChange("silence", 1), new StatusChange("handCostIncrease", 2) },
            effect.StatusChanges);
    }

    [Theory]
    [InlineData("\"amount\":1,")]
    [InlineData("\"name\":\"silence\",")]
    [InlineData("\"amountSource\":\"handCount\",")]
    public void Grouped_status_changes_reject_ambiguous_legacy_fields(string legacyField)
    {
        var json = $$"""
        {
          "id": "grouped", "name": "grouped", "rarity": 1,
          "cardType": "Skill", "cost": 1,
          "effects": [{
            "action": "debuff", "scope": "self", {{legacyField}}
            "statusChanges": [{ "name": "silence", "amount": 1 }]
          }]
        }
        """;

        Assert.Throws<CardJsonException>(() => CardJsonLoader.Parse(json));
    }

    [Fact]
    public void Versioned_schema_parses_typed_card_abilities_and_momentum()
    {
        var json = """
        {
          "id":"ability_card","name":"Ability card","activeVersion":"v1",
          "versions":[{"version":"v1","spec":{
            "rarity":2,"cardType":"Skill","cost":1,
            "effects":[{
              "action":"attack","scope":"single","side":"enemy","amount":11,
              "maxComboMin":3
            }],
            "abilities":[
              {"type":"finisher"},
              {"type":"reprise","filter":{
                "cardTypes":["Skill"],
                "maxOriginalCost":3,
                "minPaidCost":0,
                "maxPaidCost":1,
                "requiredKeywords":["wild"]
              }}
            ],
            "upgradedAbilities":[{
              "type":"contingency",
              "trigger":{"timing":"ComboReached","comboThreshold":4}
            }]
          }}]
        }
        """;

        var definition = CardJsonLoader.Parse(json);

        Assert.Equal(3, Assert.Single(definition.Effects).MaxComboMin);
        Assert.Collection(
            definition.EffectiveAbilities(upgraded: false),
            ability => Assert.IsType<FinisherAbilityDefinition>(ability),
            ability =>
            {
                var reprise = Assert.IsType<RepriseAbilityDefinition>(ability);
                Assert.Equal(new[] { CardType.Skill }, reprise.Filter.EffectiveCardTypes);
                Assert.Equal(3, reprise.Filter.MaxOriginalCost);
                Assert.Equal(0, reprise.Filter.MinPaidCost);
                Assert.Equal(1, reprise.Filter.MaxPaidCost);
                Assert.Equal(new[] { "wild" }, reprise.Filter.EffectiveRequiredKeywords);
            });
        var contingency = Assert.IsType<ContingencyAbilityDefinition>(
            Assert.Single(definition.EffectiveAbilities(upgraded: true)));
        Assert.Equal(ContingencyTriggerTiming.ComboReached, contingency.Trigger.Timing);
        Assert.Equal(4, contingency.Trigger.ComboThreshold);
    }

    [Theory]
    [InlineData("TurnStart")]
    [InlineData("BeforeEnemyAttack")]
    public void Parses_non_combo_contingency_triggers_without_threshold(string timing)
    {
        var json = """
        {
          "id":"contingency","name":"Contingency","rarity":1,"cardType":"Skill","cost":1,
          "effects":[],
          "abilities":[{"type":"contingency","trigger":{"timing":"__TIMING__"}}]
        }
        """.Replace("__TIMING__", timing);

        var ability = Assert.IsType<ContingencyAbilityDefinition>(
            Assert.Single(CardJsonLoader.Parse(json).Abilities!));

        Assert.Null(ability.Trigger.ComboThreshold);
    }

    [Theory]
    [InlineData("\"abilities\":{}")]
    [InlineData("\"abilities\":[{\"type\":\"unknown\"}]")]
    [InlineData("\"abilities\":[{\"type\":\"reprise\"}]")]
    [InlineData("\"abilities\":[{\"type\":\"reprise\",\"filter\":{\"minOriginalCost\":3,\"maxOriginalCost\":2}}]")]
    [InlineData("\"abilities\":[{\"type\":\"contingency\",\"trigger\":{\"timing\":\"ComboReached\"}}]")]
    [InlineData("\"abilities\":[{\"type\":\"contingency\",\"trigger\":{\"timing\":\"TurnStart\",\"comboThreshold\":2}}]")]
    [InlineData("\"abilities\":[{\"type\":\"finisher\"},{\"type\":\"finisher\"}]")]
    public void Rejects_malformed_typed_ability_schema(string abilityProperty)
    {
        var json = $$"""
        {
          "id":"bad_ability","name":"Bad", "rarity":1,"cardType":"Skill","cost":1,
          "effects":[],{{abilityProperty}}
        }
        """;

        Assert.Throws<CardJsonException>(() => CardJsonLoader.Parse(json));
    }
}
