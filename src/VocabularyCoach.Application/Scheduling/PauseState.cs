namespace VocabularyCoach.Application.Scheduling;

/// <summary>
/// 提醒暂停状态。Sprint 1 只负责记录状态并供托盘菜单使用，
/// Sprint 3 的调度循环会读取该状态决定是否弹出。
/// </summary>
public sealed class PauseState
{
    private readonly TimeProvider _timeProvider;
    private readonly object _sync = new();
    private DateTimeOffset? _pausedUntil;

    public PauseState(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <summary>当前是否处于暂停中；暂停到期后自动失效。</summary>
    public bool IsPaused
    {
        get
        {
            lock (_sync)
            {
                return _pausedUntil is { } until && _timeProvider.GetLocalNow() < until;
            }
        }
    }

    /// <summary>暂停截止时间（本地时间）；未暂停时为 null。</summary>
    public DateTimeOffset? PausedUntil
    {
        get
        {
            lock (_sync)
            {
                return IsPausedMutable() ? _pausedUntil : null;
            }
        }
    }

    /// <summary>暂停指定时长。</summary>
    public void Pause(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "暂停时长必须大于零。");
        }

        PauseUntil(_timeProvider.GetLocalNow() + duration);
    }

    /// <summary>暂停到指定时间点。</summary>
    public void PauseUntil(DateTimeOffset until)
    {
        lock (_sync)
        {
            _pausedUntil = until;
        }
    }

    /// <summary>暂停到明天指定时间（本地时间），用于“暂停提醒至明天”。</summary>
    public void PauseUntilTomorrow(TimeOnly resumeTime)
    {
        var now = _timeProvider.GetLocalNow();
        var tomorrow = now.Date.AddDays(1);
        PauseUntil(new DateTimeOffset(tomorrow + resumeTime.ToTimeSpan(), now.Offset));
    }

    /// <summary>暂停到明天零点（本地时间）。</summary>
    public void PauseUntilTomorrow() => PauseUntilTomorrow(TimeOnly.MinValue);

    /// <summary>立即恢复提醒。</summary>
    public void Resume()
    {
        lock (_sync)
        {
            _pausedUntil = null;
        }
    }

    private bool IsPausedMutable() =>
        _pausedUntil is { } until && _timeProvider.GetLocalNow() < until;
}
