using System;
using RoguelikeCardGame.Core.Random;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Random;

public class IRngTests
{
    [Fact]
    public void SystemRng_SameSeed_ProducesSameSequence()
    {
        var a = new SystemRng(42);
        var b = new SystemRng(42);
        for (int i = 0; i < 50; i++)
            Assert.Equal(a.NextInt(0, 100), b.NextInt(0, 100));
    }

    [Fact]
    public void SystemRng_NextInt_ReturnsInRange()
    {
        var rng = new SystemRng(1);
        for (int i = 0; i < 100; i++)
        {
            var v = rng.NextInt(5, 10);
            Assert.InRange(v, 5, 9);
        }
    }

    [Fact]
    public void SystemRng_NextDouble_ReturnsUnitInterval()
    {
        var rng = new SystemRng(1);
        for (int i = 0; i < 100; i++)
        {
            var v = rng.NextDouble();
            Assert.InRange(v, 0.0, 0.9999999999);
        }
    }

    [Fact]
    public void FakeRng_ReturnsIntsInOrder()
    {
        var rng = new FakeRng(new[] { 3, 1, 4 }, Array.Empty<double>());
        Assert.Equal(3, rng.NextInt(0, 10));
        Assert.Equal(1, rng.NextInt(0, 10));
        Assert.Equal(4, rng.NextInt(0, 10));
    }

    [Fact]
    public void FakeRng_ReturnsDoublesInOrder()
    {
        var rng = new FakeRng(Array.Empty<int>(), new[] { 0.1, 0.5, 0.9 });
        Assert.Equal(0.1, rng.NextDouble());
        Assert.Equal(0.5, rng.NextDouble());
        Assert.Equal(0.9, rng.NextDouble());
    }

    [Fact]
    public void FakeRng_ExhaustedSequence_Throws()
    {
        var rng = new FakeRng(new[] { 1 }, Array.Empty<double>());
        rng.NextInt(0, 10);
        Assert.Throws<InvalidOperationException>(() => rng.NextInt(0, 10));
    }

    [Fact]
    public void FakeRng_IntOutOfRange_Throws()
    {
        var rng = new FakeRng(new[] { 42 }, Array.Empty<double>());
        Assert.Throws<InvalidOperationException>(() => rng.NextInt(0, 10));
    }
}
