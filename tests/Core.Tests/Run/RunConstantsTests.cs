using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class RunConstantsTests
{
    [Fact]
    public void MaxAct_IsThree()
    {
        Assert.Equal(3, RunConstants.MaxAct);
    }
}
