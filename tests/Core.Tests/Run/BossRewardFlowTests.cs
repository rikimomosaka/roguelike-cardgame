using RoguelikeCardGame.Core.Data;
using RoguelikeCardGame.Core.Random;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class BossRewardFlowTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void GenerateBossReward_NonFinalAct_ReturnsRewardWithIsBossRewardTrue(int act)
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat) with { CurrentAct = act };
        var r = BossRewardFlow.GenerateBossReward(s, cat, new SystemRng(1));
        Assert.NotNull(r);
        Assert.True(r!.IsBossReward);
    }

    [Fact]
    public void GenerateBossReward_FinalAct_ReturnsNull()
    {
        var cat = EmbeddedDataLoader.LoadCatalog();
        var s = TestRunStates.FreshDefault(cat) with { CurrentAct = RunConstants.MaxAct };
        var r = BossRewardFlow.GenerateBossReward(s, cat, new SystemRng(1));
        Assert.Null(r);
    }
}
