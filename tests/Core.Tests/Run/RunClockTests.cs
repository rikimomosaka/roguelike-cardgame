using System;
using RoguelikeCardGame.Core.Run;
using Xunit;

namespace RoguelikeCardGame.Core.Tests.Run;

public class RunClockTests
{
    // 可変なフェイク時刻
    private DateTimeOffset _now = new(2026, 4, 20, 12, 0, 0, TimeSpan.Zero);
    private DateTimeOffset Now() => _now;

    [Fact]
    public void NotResumed_TotalSecondsEqualsBase()
    {
        var clock = new RunClock(Now, baseSeconds: 100);
        Assert.Equal(100L, clock.TotalSeconds);
    }

    [Fact]
    public void Resume_ThenAdvance_AddsElapsedSeconds()
    {
        var clock = new RunClock(Now, baseSeconds: 100);
        clock.Resume();
        _now = _now.AddSeconds(45);
        Assert.Equal(145L, clock.TotalSeconds);
    }

    [Fact]
    public void Pause_FreezesTotalAndSurvivesClockAdvance()
    {
        var clock = new RunClock(Now, baseSeconds: 100);
        clock.Resume();
        _now = _now.AddSeconds(45);
        clock.Pause();
        _now = _now.AddSeconds(1000); // Pause 後は進まない
        Assert.Equal(145L, clock.TotalSeconds);
    }

    [Fact]
    public void ResumeAfterPause_ContinuesFromPaused()
    {
        var clock = new RunClock(Now, baseSeconds: 100);
        clock.Resume();
        _now = _now.AddSeconds(45);
        clock.Pause();
        _now = _now.AddSeconds(1000);
        clock.Resume();
        _now = _now.AddSeconds(10);
        Assert.Equal(155L, clock.TotalSeconds);
    }

    [Fact]
    public void DoubleResume_IsIdempotent()
    {
        var clock = new RunClock(Now, baseSeconds: 0);
        clock.Resume();
        _now = _now.AddSeconds(30);
        clock.Resume(); // 2 回目は無視されるべき
        _now = _now.AddSeconds(20);
        Assert.Equal(50L, clock.TotalSeconds);
    }
}
