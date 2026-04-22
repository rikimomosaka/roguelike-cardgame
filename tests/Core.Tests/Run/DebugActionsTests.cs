using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class DebugActionsTests
{
    [Fact]
    public void ApplyDamage_SubtractsHp()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat) with { CurrentHp = 50 };
        var next = DebugActions.ApplyDamage(s, 10);
        Assert.Equal(40, next.CurrentHp);
    }

    [Fact]
    public void ApplyDamage_ClampsAtZero()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat) with { CurrentHp = 5 };
        var next = DebugActions.ApplyDamage(s, 100);
        Assert.Equal(0, next.CurrentHp);
    }
}
