using System.Collections.Immutable;
using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class ActStartActionsBestiaryTests
{
    private static readonly DataCatalog Cat = EmbeddedDataLoader.LoadCatalog();

    [Fact]
    public void ChooseRelic_TracksRelic()
    {
        var relicIds = Cat.Relics.Keys.Take(3).ToArray();
        var relicId = relicIds[0];
        var s = TestRunStates.FreshDefault(Cat) with
        {
            ActiveActStartRelicChoice = new ActStartRelicChoice(ImmutableArray.Create(relicIds)),
        };
        var after = ActStartActions.ChooseRelic(s, relicId, Cat);
        Assert.Contains(relicId, after.AcquiredRelicIds);
    }
}
