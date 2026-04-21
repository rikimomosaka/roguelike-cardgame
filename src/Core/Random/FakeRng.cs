using System;

namespace RoguelikeCardGame.Core.Random;

/// <summary>テスト用：事前に用意した int/double シーケンスを順に返す <see cref="IRng"/>。</summary>
public sealed class FakeRng : IRng
{
    private readonly int[] _ints;
    private readonly double[] _doubles;
    private int _intIndex;
    private int _doubleIndex;

    public FakeRng(int[] intSequence, double[] doubleSequence)
    {
        _ints = intSequence ?? throw new ArgumentNullException(nameof(intSequence));
        _doubles = doubleSequence ?? throw new ArgumentNullException(nameof(doubleSequence));
    }

    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (_intIndex >= _ints.Length)
            throw new InvalidOperationException("FakeRng int sequence exhausted.");
        var v = _ints[_intIndex];
        if (v < minInclusive || v >= maxExclusive)
            throw new InvalidOperationException(
                $"FakeRng value {v} out of range [{minInclusive}, {maxExclusive}).");
        _intIndex++;
        return v;
    }

    public double NextDouble()
    {
        if (_doubleIndex >= _doubles.Length)
            throw new InvalidOperationException("FakeRng double sequence exhausted.");
        return _doubles[_doubleIndex++];
    }
}
