using System.Collections.Immutable;
using RoguelikeCardGame.Core.Bestiary;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Bestiary;

public class BestiaryStateTests
{
    [Fact]
    public void Empty_HasCurrentSchemaVersion_AndEmptySets()
    {
        var s = BestiaryState.Empty;
        Assert.Equal(BestiaryState.CurrentSchemaVersion, s.SchemaVersion);
        Assert.Empty(s.DiscoveredCardBaseIds);
        Assert.Empty(s.DiscoveredRelicIds);
        Assert.Empty(s.DiscoveredPotionIds);
        Assert.Empty(s.EncounteredEnemyIds);
    }

    [Fact]
    public void RecordEquality_ByValue()
    {
        var strikeSet = ImmutableHashSet.Create("strike");
        var a = BestiaryState.Empty with
        {
            DiscoveredCardBaseIds = strikeSet,
        };
        var b = BestiaryState.Empty with
        {
            DiscoveredCardBaseIds = strikeSet,
        };
        Assert.Equal(a, b);
    }
}
