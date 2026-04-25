using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Battle.Definitions;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Data;

public class ActEncountersTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void HasAtLeastOneEncounterPerTier(int act)
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        foreach (var tier in new[] { EnemyTier.Weak, EnemyTier.Strong, EnemyTier.Elite, EnemyTier.Boss })
        {
            var any = cat.Encounters.Values.Any(e => e.Pool.Act == act && e.Pool.Tier == tier);
            Assert.True(any, $"act {act} tier {tier} encounter missing");
        }
    }
}
