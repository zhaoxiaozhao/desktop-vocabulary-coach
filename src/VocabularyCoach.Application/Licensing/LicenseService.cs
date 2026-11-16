using Microsoft.Extensions.Logging;

namespace VocabularyCoach.Application.Licensing;

/// <summary>
/// 许可证与 30 天全功能试用。
/// 试用状态同时写入文件与注册表；许可证使用 ECDSA 离线验签，不联网。
/// </summary>
public sealed class LicenseService : ILicenseService
{
    public const int TrialDays = 30;

    private static readonly TimeSpan ClockRollbackTolerance = TimeSpan.FromDays(1);

    private readonly ILicenseStore _licenseStore;
    private readonly ITrialStateStore _trialStore;
    private readonly ILicenseKeyVerifier _verifier;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LicenseService> _logger;

    private DateTimeOffset _trialStartedAt;
    private DateTimeOffset _lastSeenAt;
    private bool _clockRollback;
    private bool _isLicensed;
    private string _licenseId = string.Empty;
    private string _edition = string.Empty;
    private LicenseStatus? _lastStatus;

    public LicenseService(
        ILicenseStore licenseStore,
        ITrialStateStore trialStore,
        ILicenseKeyVerifier verifier,
        TimeProvider timeProvider,
        ILogger<LicenseService> logger)
    {
        _licenseStore = licenseStore;
        _trialStore = trialStore;
        _verifier = verifier;
        _timeProvider = timeProvider;
        _logger = logger;

        var now = _timeProvider.GetUtcNow();
        _trialStartedAt = now;
        _lastSeenAt = now;
    }

    public event EventHandler? Changed;

    public LicenseSnapshot Current => BuildSnapshot(_timeProvider.GetUtcNow());

    public bool CanUsePremiumFeatures => Current.CanUsePremiumFeatures;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        try
        {
            var savedKey = await _licenseStore.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(savedKey))
            {
                if (_verifier.TryVerify(savedKey, out var payload, out var error) && payload is not null)
                {
                    _isLicensed = true;
                    _licenseId = payload.Id;
                    _edition = payload.Edition;
                    _logger.LogInformation(
                        "许可证已加载：{LicenseId}（{Edition}）。",
                        _licenseId,
                        _edition);
                }
                else
                {
                    _logger.LogWarning("已保存的许可证无效：{Error}", error);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取许可证失败，按未激活处理。");
        }

        try
        {
            var trial = await _trialStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (trial is null)
            {
                trial = new TrialState(now, now);
                await _trialStore.SaveAsync(trial, cancellationToken).ConfigureAwait(false);
            }

            _trialStartedAt = trial.FirstStartedAt;
            _lastSeenAt = trial.LastSeenAt;

            // 系统时间明显早于上次记录，视为时钟回拨，试用直接结束。
            if (now + ClockRollbackTolerance < trial.LastSeenAt)
            {
                _clockRollback = true;
                _logger.LogWarning(
                    "检测到系统时间回拨：上次 {LastSeen:u}，当前 {Now:u}。",
                    trial.LastSeenAt,
                    now);
            }

            var saveNow = now > trial.LastSeenAt ? now : trial.LastSeenAt;
            _lastSeenAt = saveNow;
            await _trialStore
                .SaveAsync(new TrialState(trial.FirstStartedAt, saveNow), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取试用状态失败，使用本次启动时间。");
            _trialStartedAt = now;
            _lastSeenAt = now;
        }

        _lastStatus = Current.Status;
        _logger.LogInformation(
            "许可证状态：{Status}，试用结束 {TrialEndsAt:yyyy-MM-dd}，剩余 {Days} 天。",
            _lastStatus,
            _trialStartedAt.AddDays(TrialDays),
            Current.DaysRemaining);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<LicenseActivationResult> ActivateAsync(
        string licenseKey,
        CancellationToken cancellationToken = default)
    {
        if (!_verifier.TryVerify(licenseKey, out var payload, out var error) || payload is null)
        {
            return new LicenseActivationResult(false, error);
        }

        try
        {
            await _licenseStore.WriteAsync(licenseKey.Trim(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存许可证失败。");
            return new LicenseActivationResult(false, "许可证保存失败，请检查磁盘权限后重试。");
        }

        _isLicensed = true;
        _licenseId = payload.Id;
        _edition = payload.Edition;
        _lastStatus = LicenseStatus.Licensed;

        _logger.LogInformation(
            "许可证激活成功：{LicenseId}（{Edition}）。",
            _licenseId,
            _edition);

        Changed?.Invoke(this, EventArgs.Empty);

        return new LicenseActivationResult(true, "激活成功，感谢支持！");
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        try
        {
            if (now + ClockRollbackTolerance < _lastSeenAt)
            {
                _clockRollback = true;
            }

            if (now > _lastSeenAt)
            {
                _lastSeenAt = now;
                await _trialStore
                    .SaveAsync(new TrialState(_trialStartedAt, _lastSeenAt), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "刷新试用状态失败。");
        }

        var status = Current.Status;
        if (_lastStatus != status)
        {
            _lastStatus = status;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private LicenseSnapshot BuildSnapshot(DateTimeOffset now)
    {
        if (_isLicensed)
        {
            return new LicenseSnapshot(
                LicenseStatus.Licensed,
                CanUsePremiumFeatures: true,
                TrialEndsAt: null,
                DaysRemaining: 0,
                _licenseId,
                _edition);
        }

        var trialEndsAt = _trialStartedAt.AddDays(TrialDays);
        var remaining = trialEndsAt - now;
        var expired = _clockRollback || remaining <= TimeSpan.Zero;
        var daysRemaining = remaining <= TimeSpan.Zero
            ? 0
            : (int)Math.Ceiling(remaining.TotalDays);

        return new LicenseSnapshot(
            expired ? LicenseStatus.TrialExpired : LicenseStatus.Trial,
            CanUsePremiumFeatures: !expired,
            trialEndsAt,
            daysRemaining,
            LicenseId: string.Empty,
            Edition: string.Empty);
    }
}
