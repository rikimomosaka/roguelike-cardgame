using System.Linq;
using System.Text.Json;
using RoguelikeCardGame.Core.Cards;
using RoguelikeCardGame.Core.Data;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Cards;

public class CardOverrideMergerTests
{
    private const string BaseStrike = """
    {
      "id": "strike",
      "name": "ストライク",
      "displayName": null,
      "poolKinds": ["Basic"],
      "poolClass": "Universal",
      "eligibleCharacterIds": [],
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

    [Fact]
    public void Merge_unions_versions_arrays()
    {
        var overrideJson = """
        {
          "id": "strike",
          "activeVersion": "v2",
          "versions": [
            {
              "version": "v2",
              "createdAt": "2026-05-02T00:00:00Z",
              "label": "buffed",
              "spec": {
                "rarity": 1,
                "cardType": "Attack",
                "cost": 0,
                "effects": [{ "action": "attack", "scope": "single", "side": "enemy", "amount": 9 }]
              }
            }
          ]
        }
        """;
        var merged = CardOverrideMerger.Merge(BaseStrike, overrideJson);

        using var doc = JsonDocument.Parse(merged);
        var versions = doc.RootElement.GetProperty("versions").EnumerateArray().ToList();
        Assert.Equal(2, versions.Count);
        Assert.Equal("v2", doc.RootElement.GetProperty("activeVersion").GetString());

        // 結果が CardJsonLoader でも読めて、active が v2 を指していること。
        var def = CardJsonLoader.Parse(merged);
        Assert.Equal(0, def.Cost);
        Assert.Equal(9, def.Effects[0].Amount);
    }

    [Fact]
    public void Merge_override_version_replaces_base_version_with_same_id()
    {
        // override v1 が base v1 を完全に置換すること。
        var overrideJson = """
        {
          "id": "strike",
          "versions": [
            {
              "version": "v1",
              "createdAt": "2026-05-02T00:00:00Z",
              "label": "tweak",
              "spec": {
                "rarity": 1,
                "cardType": "Attack",
                "cost": 1,
                "effects": [{ "action": "attack", "scope": "single", "side": "enemy", "amount": 99 }]
              }
            }
          ]
        }
        """;
        var merged = CardOverrideMerger.Merge(BaseStrike, overrideJson);
        using var doc = JsonDocument.Parse(merged);

        var versions = doc.RootElement.GetProperty("versions").EnumerateArray().ToList();
        Assert.Single(versions);

        var def = CardJsonLoader.Parse(merged);
        Assert.Equal(99, def.Effects[0].Amount);
    }

    [Fact]
    public void Merge_id_mismatch_throws()
    {
        var overrideJson = """
        {
          "id": "defend",
          "versions": []
        }
        """;
        Assert.Throws<CardJsonException>(() => CardOverrideMerger.Merge(BaseStrike, overrideJson));
    }

    [Fact]
    public void Merge_override_without_activeVersion_keeps_base()
    {
        var overrideJson = """
        {
          "id": "strike",
          "versions": [
            {
              "version": "v2",
              "createdAt": "2026-05-02T00:00:00Z",
              "spec": {
                "rarity": 1,
                "cardType": "Attack",
                "cost": 0,
                "effects": []
              }
            }
          ]
        }
        """;
        var merged = CardOverrideMerger.Merge(BaseStrike, overrideJson);
        using var doc = JsonDocument.Parse(merged);
        Assert.Equal("v1", doc.RootElement.GetProperty("activeVersion").GetString());
    }

    [Fact]
    public void Merge_version_only_override_preserves_base_pool_metadata()
    {
        var overrideJson = """
        {
          "id": "strike",
          "activeVersion": "v2",
          "versions": [{
            "version": "v2",
            "spec": { "rarity": 1, "cardType": "Attack", "cost": 0, "effects": [] }
          }]
        }
        """;

        var definition = CardJsonLoader.Parse(CardOverrideMerger.Merge(BaseStrike, overrideJson));

        Assert.Equal(new[] { CardPoolKind.Basic }, definition.EffectivePoolKinds);
        Assert.Equal(CardPoolClass.Universal, definition.PoolClass);
    }

    [Fact]
    public void Merge_explicit_empty_poolKinds_removes_inherited_pool_metadata()
    {
        var overrideJson = """
        {
          "id": "strike",
          "poolKinds": [],
          "activeVersion": "v1",
          "versions": []
        }
        """;

        var definition = CardJsonLoader.Parse(CardOverrideMerger.Merge(BaseStrike, overrideJson));

        Assert.Empty(definition.EffectivePoolKinds);
        Assert.Null(definition.PoolClass);
        Assert.Empty(definition.EffectiveEligibleCharacterIds);
    }

    [Fact]
    public void Merge_explicit_poolKinds_uses_only_override_group_members()
    {
        var overrideJson = """
        {
          "id": "strike",
          "poolKinds": ["Reward"],
          "poolClass": "Striker",
          "activeVersion": "v1",
          "versions": []
        }
        """;

        var definition = CardJsonLoader.Parse(CardOverrideMerger.Merge(BaseStrike, overrideJson));

        Assert.Equal(new[] { CardPoolKind.Reward }, definition.EffectivePoolKinds);
        Assert.Equal(CardPoolClass.Striker, definition.PoolClass);
        Assert.Empty(definition.EffectiveEligibleCharacterIds);
    }
}
