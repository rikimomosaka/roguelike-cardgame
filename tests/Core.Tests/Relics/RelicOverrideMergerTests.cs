using System.Linq;
using System.Text.Json;
using RoguelikeCardGame.Core.Relics;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Relics;

public class RelicOverrideMergerTests
{
    private const string BaseAnchor = """
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
            "description": "原文。",
            "effects": [{ "action": "gainMaxHp", "scope": "self", "amount": 8 }],
            "implemented": true
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
          "id": "anchor",
          "activeVersion": "v2",
          "versions": [
            {
              "version": "v2",
              "createdAt": "2026-05-02T01:00:00Z",
              "label": "buffed",
              "spec": {
                "rarity": 2,
                "trigger": "Passive",
                "description": "強化版。",
                "effects": [{ "action": "gainMaxHp", "scope": "self", "amount": 20 }],
                "implemented": true
              }
            }
          ]
        }
        """;
        var merged = RelicOverrideMerger.Merge(BaseAnchor, overrideJson);
        using var doc = JsonDocument.Parse(merged);
        var versions = doc.RootElement.GetProperty("versions").EnumerateArray().ToList();
        Assert.Equal(2, versions.Count);
        Assert.Equal("v2", doc.RootElement.GetProperty("activeVersion").GetString());

        // 結果が RelicJsonLoader でも読めて、active が v2 を指していること。
        var def = RelicJsonLoader.Parse(merged);
        Assert.Equal(RelicTrigger.Passive, def.Trigger);
        Assert.Equal(20, def.Effects[0].Amount);
    }

    [Fact]
    public void Merge_override_version_replaces_base_version_with_same_id()
    {
        // override v1 が base v1 を完全に置換すること。
        var overrideJson = """
        {
          "id": "anchor",
          "versions": [
            {
              "version": "v1",
              "createdAt": "2026-05-02T01:00:00Z",
              "label": "tweak",
              "spec": {
                "rarity": 1,
                "trigger": "OnPickup",
                "effects": [{ "action": "gainMaxHp", "scope": "self", "amount": 99 }]
              }
            }
          ]
        }
        """;
        var merged = RelicOverrideMerger.Merge(BaseAnchor, overrideJson);
        using var doc = JsonDocument.Parse(merged);

        var versions = doc.RootElement.GetProperty("versions").EnumerateArray().ToList();
        Assert.Single(versions);

        var def = RelicJsonLoader.Parse(merged);
        Assert.Equal(99, def.Effects[0].Amount);
    }

    [Fact]
    public void Merge_id_mismatch_throws()
    {
        var overrideJson = """
        {
          "id": "lantern",
          "versions": []
        }
        """;
        Assert.Throws<RelicJsonException>(() => RelicOverrideMerger.Merge(BaseAnchor, overrideJson));
    }

    [Fact]
    public void Merge_override_without_activeVersion_keeps_base()
    {
        var overrideJson = """
        {
          "id": "anchor",
          "versions": [
            {
              "version": "v2",
              "spec": {
                "rarity": 1,
                "trigger": "OnPickup",
                "effects": []
              }
            }
          ]
        }
        """;
        var merged = RelicOverrideMerger.Merge(BaseAnchor, overrideJson);
        using var doc = JsonDocument.Parse(merged);
        Assert.Equal("v1", doc.RootElement.GetProperty("activeVersion").GetString());
    }
}
