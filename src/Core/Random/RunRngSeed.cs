using System;

namespace RoguelikeCardGame.Core.Random;

/// <summary>
/// A stable namespace for random streams derived from one run seed.
/// Explicit numeric values are part of the replay/save contract; do not reorder them.
/// </summary>
public enum RunRngPurpose : ulong
{
    UnknownKind = 1,
    NodeContent = 2,
    Battle = 3,
    BattleReward = 4,
    RewardReroll = 5,
    EventChoice = 6,
    ActStartRelic = 7,
    ActTransition = 8,
}

/// <summary>
/// Derives deterministic, domain-separated <see cref="SystemRng"/> seeds.
/// This deliberately avoids <see cref="string.GetHashCode()"/>, whose result is not
/// stable across processes or runtime versions.
/// </summary>
public static class RunRngSeed
{
    public static int Derive(
        ulong runSeed,
        int act,
        int nodeId,
        RunRngPurpose purpose,
        int sequence = 0)
    {
        if (act < 1) throw new ArgumentOutOfRangeException(nameof(act));
        if (!Enum.IsDefined(purpose)) throw new ArgumentOutOfRangeException(nameof(purpose));

        ulong mixed = Mix(runSeed ^ 0x52554E5F524E475FUL); // "RUN_RNG_"
        mixed = Mix(mixed ^ unchecked((ulong)(uint)act));
        mixed = Mix(mixed ^ unchecked((ulong)(uint)nodeId));
        mixed = Mix(mixed ^ (ulong)purpose);
        mixed = Mix(mixed ^ unchecked((ulong)(uint)sequence));

        return unchecked((int)(mixed ^ (mixed >> 32)));
    }

    // SplitMix64 finalizer: compact, stable and suitable for separating seed domains.
    private static ulong Mix(ulong value)
    {
        value ^= value >> 30;
        value *= 0xBF58476D1CE4E5B9UL;
        value ^= value >> 27;
        value *= 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
