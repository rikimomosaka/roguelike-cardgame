using System;

namespace RoguelikeCardGame.Core.Run;

/// <summary>ラン中の経過秒数を追跡する。Pause/Resume で進行を切り替え、TotalSeconds で合算値を返す。</summary>
public sealed class RunClock
{
    private readonly Func<DateTimeOffset> _now;
    private long _baseSeconds;
    private DateTimeOffset? _resumedAt;

    public RunClock(Func<DateTimeOffset> nowProvider, long baseSeconds = 0)
    {
        _now = nowProvider ?? throw new ArgumentNullException(nameof(nowProvider));
        _baseSeconds = baseSeconds;
    }

    /// <summary>計測を再開する。すでに再開中なら何もしない（冪等）。</summary>
    public void Resume()
    {
        if (_resumedAt is null) _resumedAt = _now();
    }

    /// <summary>計測を一時停止し、現在の累計を内部ベースに畳み込む。</summary>
    public void Pause()
    {
        if (_resumedAt is not null)
        {
            _baseSeconds = TotalSeconds;
            _resumedAt = null;
        }
    }

    /// <summary>現在の累計秒数（ベース + 再開中なら現在セッション経過分）。</summary>
    public long TotalSeconds =>
        _resumedAt is null
            ? _baseSeconds
            : _baseSeconds + (long)(_now() - _resumedAt.Value).TotalSeconds;
}
