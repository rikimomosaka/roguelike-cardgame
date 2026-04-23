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

    [Fact]
    public void Serialize_EmitsIdsInAscendingOrder()
    {
        var s = BestiaryState.Empty with
        {
            DiscoveredCardBaseIds = ImmutableHashSet.Create("zap", "anger", "strike"),
        };
        var json = BestiaryStateSerializer.Serialize(s);
        int iAnger = json.IndexOf("\"anger\"", System.StringComparison.Ordinal);
        int iStrike = json.IndexOf("\"strike\"", System.StringComparison.Ordinal);
        int iZap = json.IndexOf("\"zap\"", System.StringComparison.Ordinal);
        Assert.True(iAnger < iStrike && iStrike < iZap, $"IDs not sorted: {json}");
    }
}
