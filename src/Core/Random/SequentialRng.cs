namespace RoguelikeCardGame.Core.Random;

/// <summary>シード付き決定論的乱数（xorshift64）。テストおよび再現性が必要な場面で使う。</summary>
public sealed class SequentialRng : IRng
{
    private ulong _state;

    public SequentialRng(ulong seed)
    {
        // xorshift64 は 0 が禁止シード
        _state = seed == 0UL ? 1UL : seed;
    }

    private ulong Next()
    {
        _state ^= _state << 13;
        _state ^= _state >> 7;
        _state ^= _state << 17;
        return _state;
    }

    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive) return minInclusive;
        long range = (long)maxExclusive - minInclusive;
        return minInclusive + (int)(Next() % (ulong)range);
    }

    public double NextDouble() => (Next() >> 11) * (1.0 / (1UL << 53));
}
