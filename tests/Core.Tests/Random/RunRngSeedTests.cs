using System;
using RoguelikeCardGame.Core.Random;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Random;

public sealed class RunRngSeedTests
{
    [Fact]
    public void Derive_IsStableForKnownCoordinates()
    {
        Assert.Equal(
            1870278535,
            RunRngSeed.Derive(42UL, 2, 17, RunRngPurpose.Battle));
    }

    [Fact]
    public void Derive_SeparatesActNodePurposeAndSequence()
    {
        var baseline = RunRngSeed.Derive(42UL, 1, 7, RunRngPurpose.NodeContent);

        Assert.NotEqual(baseline, RunRngSeed.Derive(42UL, 2, 7, RunRngPurpose.NodeContent));
        Assert.NotEqual(baseline, RunRngSeed.Derive(42UL, 1, 8, RunRngPurpose.NodeContent));
        Assert.NotEqual(baseline, RunRngSeed.Derive(42UL, 1, 7, RunRngPurpose.UnknownKind));
        Assert.NotEqual(baseline, RunRngSeed.Derive(42UL, 1, 7, RunRngPurpose.NodeContent, 1));
    }

    [Fact]
    public void Derive_RejectsInvalidCoordinates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RunRngSeed.Derive(1UL, 0, 0, RunRngPurpose.Battle));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RunRngSeed.Derive(1UL, 1, 0, (RunRngPurpose)999));
    }
}
