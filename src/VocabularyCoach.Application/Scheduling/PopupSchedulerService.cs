using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VocabularyCoach.Application.Abstractions;
using VocabularyCoach.Application.Learning;
using VocabularyCoach.Application.Licensing;
using VocabularyCoach.Application.Settings;
using VocabularyCoach.Core.Enums;

namespace VocabularyCoach.Application.Scheduling;

/// <summary>
/// 后台弹出调度器：随机间隔 → 勿扰/系统状态/频率检查 → 展示卡片 → 重新安排。
/// </summary>
/// <remarks>
/// 设计要点：
/// 1. 等待分片进行，暂停与退出可在数秒内生效，不使用 Thread.Sleep 或忙循环。
/// 2. 状态不允许时不立即补弹，而是延迟后重新检查；真正弹出后总是重新随机等待。
/// 3. 所有决策（勿扰、空闲、全屏、锁屏、频率）在 Application 层完成，系统 API 由抽象注入。
/// 4. 配置从 <see cref="ISettingsService"/> 实时读取，设置保存后无需重启。
/// </remarks>
public sealed class PopupSchedulerService : BackgroundService, ISchedulerStatus
{
    private static readonly TimeSpan MaxWaitSlice = TimeSpan.FromSeconds(15);

    private readonly ISettingsService _settings;
    private readonly ISystemStateMonitor _systemStateMonitor;
    private readonly PopupRateLimiter _rateLimiter;
    private readonly PauseState _pauseState;
    private readonly WordCardService _wordCardService;
    private readonly ILicenseService _license;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PopupSchedulerService> _logger;
    private readonly object _nextCheckLock = new();

    private DateTimeOffset? _nextCheckAt;
    private string? _lastSkipReason;
    private bool _isDisposed;
    private TaskCompletionSource _wakeSignal = CreateWakeSignal();

    private static TaskCompletionSource CreateWakeSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void ResetWakeSignal() => Interlocked.Exchange(ref _wakeSignal, CreateWakeSignal());

    public PopupSchedulerService(
        ISettingsService settings,
        ISystemStateMonitor systemStateMonitor,
        PopupRateLimiter rateLimiter,
        PauseState pauseState,
        WordCardService wordCardService,
        ILicenseService license,
        TimeProvider timeProvider,
        ILogger<PopupSchedulerService> logger)
    {
        _settings = settings;
        _systemStateMonitor = systemStateMonitor;
        _rateLimiter = rateLimiter;
        _pauseState = pauseState;
        _wordCardService = wordCardService;
        _license = license;
        _timeProvider = timeProvider;
        _logger = logger;
        _settings.Changed += OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        _wakeSignal.TrySetResult();
        _lastSkipReason = null;
    }

    public bool IsRunning => !_isDisposed && ExecuteTask is { IsCompleted: false };

    public DateTimeOffset? NextCheckAt
    {
        get
        {
            lock (_nextCheckLock)
            {
                return _nextCheckAt;
            }
        }
    }

    public string DescribeStatus()
    {
        if (!_settings.Scheduler.Enabled)
        {
            return "自动提醒：已关闭";
        }

        if (_pauseState.IsPaused)
        {
            return "自动提醒：已暂停";
        }

        var next = NextCheckAt;
        return next is null
            ? "自动提醒：已启用（等待调度）"
            : $"自动提醒：已启用，下次检查约 {next.Value:HH:mm}";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = _settings.Scheduler;
        _logger.LogInformation(
            "弹出调度器已启动：间隔 {Min:0.##}-{Max:0.##} 分钟，每小时上限 {Limit} 次，勿扰 {QuietStart}-{QuietEnd}。",
            settings.MinIntervalMinutes,
            settings.MaxIntervalMinutes,
            settings.MaxPopupsPerHour,
            settings.QuietHoursStart,
            settings.QuietHoursEnd);

        try
        {
            TimeSpan? pendingDelay = null;

            while (!stoppingToken.IsCancellationRequested)
            {
                await WaitWhilePausedAsync(stoppingToken).ConfigureAwait(false);
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                var delay = pendingDelay ?? _settings.Scheduler.NextInterval(Random.Shared);
                pendingDelay = null;
                ResetWakeSignal();
                await WaitAsync(delay, stoppingToken).ConfigureAwait(false);

                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                if (_pauseState.IsPaused)
                {
                    continue;
                }

                if (!_license.CanUsePremiumFeatures)
                {
                    _lastSkipReason = "试用已结束，请激活许可证";
                    _logger.LogInformation("跳过本次自动弹卡：试用已结束，请激活许可证。");
                    pendingDelay = TimeSpan.FromMinutes(30);
                    continue;
                }

                if (!await TryShowPopupAsync(stoppingToken).ConfigureAwait(false))
                {
                    // 状态不允许：短暂延迟后重新检查，不连续补弹。
                    pendingDelay = TimeSpan.FromMinutes(
                        Math.Max(0.001, _settings.Scheduler.RecheckDelayMinutes));
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 正常停止路径。
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "弹出调度器异常终止。");
            throw;
        }
        finally
        {
            SetNextCheckAt(null);
            _logger.LogInformation("弹出调度器已停止。");
        }
    }

    private TimeSpan GetWaitSlice()
    {
        var maxIntervalMs = Math.Max(50, _settings.Scheduler.MaxIntervalMinutes * 60_000);
        return TimeSpan.FromMilliseconds(Math.Min(MaxWaitSlice.TotalMilliseconds, maxIntervalMs));
    }

    private async Task WaitWhilePausedAsync(CancellationToken stoppingToken)
    {
        if (!_pauseState.IsPaused)
        {
            return;
        }

        _logger.LogInformation("提醒已暂停，调度器等待恢复。");

        while (_pauseState.IsPaused && !stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(GetWaitSlice(), stoppingToken).ConfigureAwait(false);
        }

        if (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("提醒已恢复，重新进入随机调度（不会立即补弹）。");
        }
    }

    private async Task WaitAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        var deadline = _timeProvider.GetUtcNow() + delay;
        var wakeTask = _wakeSignal.Task;
        SetNextCheckAt(_timeProvider.GetLocalNow() + delay);

        while (!stoppingToken.IsCancellationRequested && !_pauseState.IsPaused)
        {
            var remaining = deadline - _timeProvider.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            var slice = GetWaitSlice();
            var delayTask = Task.Delay(remaining < slice ? remaining : slice, stoppingToken);
            var completed = await Task.WhenAny(delayTask, wakeTask).ConfigureAwait(false);

            if (completed == wakeTask)
            {
                // 设置已变更：立即结束本轮等待，按新配置重新调度。
                return;
            }

            await delayTask.ConfigureAwait(false);
        }
    }

    /// <summary>尝试自动弹卡；返回 true 表示本轮已结束（正常间隔），false 表示需要稍后重试。</summary>
    private async Task<bool> TryShowPopupAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Scheduler.Enabled)
        {
            LogSkip("提醒已在设置中关闭");
            return false;
        }

        var localNow = _timeProvider.GetLocalNow();

        if (_settings.QuietHours.IsQuiet(TimeOnly.FromDateTime(localNow.DateTime)))
        {
            LogSkip("当前处于勿扰时段");
            return false;
        }

        var systemState = await _systemStateMonitor.GetStateAsync(stoppingToken).ConfigureAwait(false);
        if (!systemState.IsEligibleForPopup)
        {
            LogSkip(systemState.Describe());
            return false;
        }

        if (!_rateLimiter.CanShow())
        {
            LogSkip($"最近一小时已弹出 {_rateLimiter.CountInLastHour()}/{_rateLimiter.MaxPerHour} 次");
            return false;
        }

        try
        {
            var shownAt = _timeProvider.GetUtcNow();
            var result = await _wordCardService.ShowNextCardAsync(LearningSource.Popup, stoppingToken).ConfigureAwait(false);

            if (result is not null)
            {
                _rateLimiter.RecordPopup(shownAt);
                _lastSkipReason = null;
                _logger.LogInformation("自动弹出完成，结果 {Result}。", result);
            }

            return true;
        }
        catch (InvalidOperationException ex)
        {
            // 用户刚好手动弹出了卡片：不打断，按正常节奏等待下一轮。
            _logger.LogInformation(ex, "已有卡片正在显示，跳过本次自动弹卡。");
            return true;
        }
    }

    private void LogSkip(string reason)
    {
        if (_lastSkipReason == reason)
        {
            _logger.LogDebug("继续等待：{Reason}", reason);
            return;
        }

        _lastSkipReason = reason;
        _logger.LogInformation("跳过本次自动弹卡：{Reason}", reason);
    }

    private void SetNextCheckAt(DateTimeOffset? value)
    {
        lock (_nextCheckLock)
        {
            _nextCheckAt = value;
        }
    }

    public override void Dispose()
    {
        _isDisposed = true;
        _settings.Changed -= OnSettingsChanged;
        base.Dispose();
    }
}
