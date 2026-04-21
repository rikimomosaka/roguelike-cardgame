using SysRandom = System.Random;

namespace RoguelikeCardGame.Core.Random;

/// <summary><see cref="SysRandom"/> をラップする <see cref="IRng"/> 実装。</summary>
public sealed class SystemRng : IRng
{
    private readonly SysRandom _random;

    public SystemRng(int seed)
    {
        _random = new SysRandom(seed);
    }

    public int NextInt(int minInclusive, int maxExclusive) =>
        _random.Next(minInclusive, maxExclusive);

    public double NextDouble() => _random.NextDouble();
}
