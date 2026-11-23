using RoguelikeCardGame.Core.Battle.Statuses;
using RoguelikeCardGame.Core.Data;

namespace RoguelikeCardGame.Core.Tests.Data;

public sealed class ContentAuthoringCatalogTests
{
    [Fact]
    public void Every_runtime_status_has_the_canonical_authoring_label()
    {
        foreach (var status in StatusDefinition.All)
        {
            Assert.True(
                ContentAuthoringCatalog.DisplayNames.TryGetValue(
                    $"statuses:{status.Id}", out var authoringName),
                $"Missing authoring label for status '{status.Id}'.");
            Assert.Equal(BattleStatusTextCatalog.Get(status.Id).Name, authoringName);
        }
    }

    [Fact]
    public void Every_exposed_action_and_amount_source_has_a_display_label()
    {
        foreach (var action in ContentAuthoringCatalog.EffectActions)
            Assert.Contains($"actions:{action}", ContentAuthoringCatalog.DisplayNames.Keys);

        foreach (var source in ContentAuthoringCatalog.AmountSources)
            Assert.Contains($"amountSources:{source}", ContentAuthoringCatalog.DisplayNames.Keys);
    }
}
