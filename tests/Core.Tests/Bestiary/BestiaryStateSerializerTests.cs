using System.Collections.Immutable;
using RoguelikeCardGame.Core.Bestiary;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Bestiary;

public class BestiaryStateSerializerTests
{
    [Fact]
    public void Roundtrip_PreservesSets()
    {
        var original = BestiaryState.Empty with
        {
            DiscoveredCardBaseIds = ImmutableHashSet.Create("strike", "defend"),
            DiscoveredRelicIds = ImmutableHashSet.Create("burning_blood"),
            DiscoveredPotionIds = ImmutableHashSet.Create("fire_potion"),
            EncounteredEnemyIds = ImmutableHashSet.Create("jaw_worm"),
        };
        var json = BestiaryStateSerializer.Serialize(original);
        var restored = BestiaryStateSerializer.Deserialize(json);
        Assert.Equal(original.SchemaVersion, restored.SchemaVersion);
        Assert.True(original.DiscoveredCardBaseIds.SetEquals(restored.DiscoveredCardBaseIds));
        Assert.True(original.DiscoveredRelicIds.SetEquals(restored.DiscoveredRelicIds));
        Assert.True(original.DiscoveredPotionIds.SetEquals(restored.DiscoveredPotionIds));
        Assert.True(original.EncounteredEnemyIds.SetEquals(restored.EncounteredEnemyIds));
    }

    [Theory]
    [InlineData("discoveredCardBaseIds")]
    [InlineData("discoveredRelicIds")]
    [InlineData("discoveredPotionIds")]
    [InlineData("encounteredEnemyIds")]
    public void Serialize_EmitsIdsInAscendingOrder(string fieldName)
    {
        var unsorted = ImmutableHashSet.Create("zap", "anger", "strike");
        var s = fieldName switch
        {
            "discoveredCardBaseIds" => BestiaryState.Empty with { DiscoveredCardBaseIds = unsorted },
            "discoveredRelicIds" => BestiaryState.Empty with { DiscoveredRelicIds = unsorted },
            "discoveredPotionIds" => BestiaryState.Empty with { DiscoveredPotionIds = unsorted },
            "encounteredEnemyIds" => BestiaryState.Empty with { EncounteredEnemyIds = unsorted },
            _ => throw new System.ArgumentException($"Unknown field: {fieldName}")
        };
        var json = BestiaryStateSerializer.Serialize(s);
        int iAnger = json.IndexOf("\"anger\"", System.StringComparison.Ordinal);
        int iStrike = json.IndexOf("\"strike\"", System.StringComparison.Ordinal);
        int iZap = json.IndexOf("\"zap\"", System.StringComparison.Ordinal);
        Assert.True(iAnger >= 0 && iStrike >= 0 && iZap >= 0, $"IDs not found: {json}");
        Assert.True(iAnger < iStrike && iStrike < iZap, $"IDs not sorted: {json}");
    }

    [Fact]
    public void Deserialize_NonStringArrayElement_IsIgnored()
    {
        // JSON with a numeric element mixed in — should not crash.
        var json = """
        {
            "schemaVersion": 1,
            "discoveredCardBaseIds": ["strike", 42, "defend"],
            "discoveredRelicIds": [],
            "discoveredPotionIds": [],
            "encounteredEnemyIds": []
        }
        """;
        var restored = BestiaryStateSerializer.Deserialize(json);
        Assert.Contains("strike", restored.DiscoveredCardBaseIds);
        Assert.Contains("defend", restored.DiscoveredCardBaseIds);
        Assert.Equal(2, restored.DiscoveredCardBaseIds.Count);
    }
}
