using System.Linq;
using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class RunStateNewSoloRunTests
{
    [Fact]
    public void NewSoloRun_SeedsSeenCardsWithInitialDeckBaseIds()
    {
        var state = TestRunStates.FreshDefault(EmbeddedDataLoader.LoadCatalog());
        var deckIds = state.Deck.Select(c => c.Id).Distinct().OrderBy(s => s).ToArray();
        var seen = state.SeenCardBaseIds.OrderBy(s => s).ToArray();
        Assert.Equal(deckIds, seen);
    }
}
