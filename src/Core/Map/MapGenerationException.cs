using System;

namespace RoguelikeCardGame.Core.Map;

/// <summary>
/// 指定回数の再生成試行でも制約を満たすマップが生成できなかったことを示す例外。
/// </summary>
public sealed class MapGenerationException : Exception
{
    public int AttemptCount { get; }
    public string FailureReason { get; }

    public MapGenerationException(int attemptCount, string failureReason)
        : base($"Map generation failed after {attemptCount} attempts: {failureReason}")
    {
        AttemptCount = attemptCount;
        FailureReason = failureReason;
    }

    public MapGenerationException(int attemptCount, string failureReason, Exception inner)
        : base($"Map generation failed after {attemptCount} attempts: {failureReason}", inner)
    {
        AttemptCount = attemptCount;
        FailureReason = failureReason;
    }
}
