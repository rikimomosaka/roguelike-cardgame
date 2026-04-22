using RoguelikeCardGame.Core.Data;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Events;

public class EventCatalogTests
{
    [Fact]
    public void LoadCatalog_IncludesThreeSeedEvents()
    {
        var catalog = EmbeddedDataLoader.LoadCatalog();
        Assert.Contains("blessing_fountain", catalog.Events.Keys);
        Assert.Contains("shady_merchant", catalog.Events.Keys);
        Assert.Contains("old_library", catalog.Events.Keys);
    }
}
