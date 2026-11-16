using VocabularyCoach.Application.Settings;

namespace VocabularyCoach.Application.Scheduling;

/// <summary>
/// 每小时弹出次数限制（滑动窗口，仅统计自动弹卡）。
/// 上限从设置服务实时读取，修改设置后无需重启。
/// </summary>
public sealed class PopupRateLimiter
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private readonly ISettingsService _settings;
    private readonly TimeProvider _timeProvider;
    private readonly Queue<DateTimeOffset> _popups = new();
    private readonly object _sync = new();

    public PopupRateLimiter(ISettingsService settings, TimeProvider timeProvider)
    {
        _settings = settings;
        _timeProvider = timeProvider;
    }

    public int MaxPerHour => Math.Max(1, _settings.Scheduler.MaxPopupsPerHour);

    public int CountInLastHour()
    {
        lock (_sync)
        {
            Trim(_timeProvider.GetUtcNow());
            return _popups.Count;
        }
    }

    /// <summary>当前是否还允许自动弹出。</summary>
    public bool CanShow()
    {
        lock (_sync)
        {
            Trim(_timeProvider.GetUtcNow());
            return _popups.Count < MaxPerHour;
        }
    }

    /// <summary>记录一次自动弹出（使用卡片开始展示的时间）。</summary>
    public void RecordPopup(DateTimeOffset shownAt)
    {
        lock (_sync)
        {
            _popups.Enqueue(shownAt);
            Trim(shownAt);
        }
    }

    private void Trim(DateTimeOffset now)
    {
        while (_popups.Count > 0 && now - _popups.Peek() >= Window)
        {
            _popups.Dequeue();
        }
    }
}
