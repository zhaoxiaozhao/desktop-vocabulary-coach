using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VocabularyCoach.Application.Ai;
using VocabularyCoach.Core.ValueObjects;

namespace VocabularyCoach.Application.Settings;

/// <summary>
/// 设置服务实现。设置以 JSON 存在 AppSettings 表的一个键下，
/// 默认值来自 appsettings.json（Scheduler / Recommendation / Card 节点）。
/// </summary>
public sealed class SettingsService : ISettingsService
{
    public const string StorageKey = "user.settings.v1";
    public const string InitializedKey = "app.initialized";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ISettingsStore _store;
    private readonly SchedulerOptions _schedulerDefaults;
    private readonly RecommendationOptions _recommendationDefaults;
    private readonly CardDisplayOptions _cardDefaults;
    private readonly ILogger<SettingsService> _logger;

    private UserSettings _current;

    public SettingsService(
        ISettingsStore store,
        IOptions<SchedulerOptions> schedulerDefaults,
        IOptions<RecommendationOptions> recommendationDefaults,
        IOptions<CardDisplayOptions> cardDefaults,
        ILogger<SettingsService> logger)
    {
        _store = store;
        _schedulerDefaults = schedulerDefaults.Value;
        _recommendationDefaults = recommendationDefaults.Value;
        _cardDefaults = cardDefaults.Value;
        _logger = logger;

        _current = CreateDefaults();
        ApplyDerived(_current);
    }

    public UserSettings Current => _current;

    public SchedulerOptions Scheduler { get; private set; } = new();

    public RecommendationOptions Recommendation { get; private set; } = new();

    public CardDisplayOptions Card { get; private set; } = new();

    public QuietHours QuietHours { get; private set; } = QuietHours.Disabled;

    public bool IsInitialized { get; private set; }

    public event EventHandler? Changed;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var initialized = await _store.GetAsync(InitializedKey, cancellationToken).ConfigureAwait(false);
            IsInitialized = initialized == "1";

            var json = await _store.GetAsync(StorageKey, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(json))
            {
                _current = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions) ?? CreateDefaults();
                AiConnectionSettings.Ensure(_current);
                ApplyDerived(_current);
                _logger.LogInformation("已加载用户设置（{Key}）。", StorageKey);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载用户设置失败，使用 appsettings.json 默认值。");
        }

        _current = CreateDefaults();
        AiConnectionSettings.Ensure(_current);
        ApplyDerived(_current);
    }

    public async Task SaveAsync(UserSettings settings, CancellationToken cancellationToken)
    {
        var normalized = Normalize(settings);
        AiConnectionSettings.Ensure(normalized);
        var json = JsonSerializer.Serialize(normalized, JsonOptions);

        await _store.SetAsync(StorageKey, json, cancellationToken).ConfigureAwait(false);

        _current = normalized;
        ApplyDerived(normalized);
        _logger.LogInformation("用户设置已保存。");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task MarkInitializedAsync(CancellationToken cancellationToken)
    {
        await _store.SetAsync(InitializedKey, "1", cancellationToken).ConfigureAwait(false);
        IsInitialized = true;
        _logger.LogInformation("首次启动向导已完成。");
    }

    private UserSettings CreateDefaults() => new()    {
        RemindersEnabled = true,
        MinIntervalMinutes = _schedulerDefaults.MinIntervalMinutes,
        MaxIntervalMinutes = _schedulerDefaults.MaxIntervalMinutes,
        MaxPopupsPerHour = _schedulerDefaults.MaxPopupsPerHour,
        QuietHoursStart = _schedulerDefaults.QuietHoursStart,
        QuietHoursEnd = _schedulerDefaults.QuietHoursEnd,
        IdleThresholdMinutes = _schedulerDefaults.IdleThresholdMinutes,
        EnableIdleDetection = _schedulerDefaults.EnableIdleDetection,
        DetectFullscreen = _schedulerDefaults.DetectFullscreen,
        RecheckDelayMinutes = _schedulerDefaults.RecheckDelayMinutes,
        RecommendationMode = RecommendationMode.Smart,
        DifficultyMode = DifficultyMode.Adaptive,
        ManualDifficulty = 2,
        NewWordRatio = _recommendationDefaults.NewWordRatio,
        PauseUntilTomorrowTime = "00:00",
        Theme = CardTheme.Light,
        CaptureEnabled = true,
        CaptureSilentAdd = false,
        CaptureShowNotification = true,
        StartWithWindows = false,
        ShowPhonetic = _cardDefaults.ShowPhonetic,
        AutoExpand = _cardDefaults.AutoExpand,
        Animate = _cardDefaults.Animate,
        AiEnabled = false,
        AiPreset = AiPreset.OpenAi,
        AiBaseUrl = AiPresetCatalog.Get(AiPreset.OpenAi).BaseUrl,
        AiModel = AiPresetCatalog.Get(AiPreset.OpenAi).DefaultModel,
    };

    private static UserSettings Normalize(UserSettings settings)
    {
        var normalized = settings.Clone();

        normalized.MinIntervalMinutes = Math.Clamp(normalized.MinIntervalMinutes, 0.001, 24 * 60);
        normalized.MaxIntervalMinutes = Math.Clamp(
            normalized.MaxIntervalMinutes,
            normalized.MinIntervalMinutes,
            7 * 24 * 60);
        normalized.MaxPopupsPerHour = Math.Clamp(normalized.MaxPopupsPerHour, 1, 60);
        normalized.IdleThresholdMinutes = Math.Clamp(normalized.IdleThresholdMinutes, 0, 24 * 60);
        normalized.RecheckDelayMinutes = Math.Clamp(normalized.RecheckDelayMinutes, 0.001, 24 * 60);
        normalized.ManualDifficulty = Math.Clamp(normalized.ManualDifficulty, 1, 4);
        normalized.NewWordRatio = Math.Clamp(normalized.NewWordRatio, 0, 1);
        normalized.AiBaseUrl = (normalized.AiBaseUrl ?? string.Empty).Trim();
        normalized.AiModel = (normalized.AiModel ?? string.Empty).Trim();
        normalized.CaptureHotkey = (normalized.CaptureHotkey ?? string.Empty).Trim();
        normalized.TranslateHotkey = (normalized.TranslateHotkey ?? string.Empty).Trim();
        normalized.SentencePracticePopupChancePercent = Math.Clamp(
            normalized.SentencePracticePopupChancePercent,
            0,
            100);
        normalized.SentencePracticePopupSentenceCount = Math.Clamp(
            normalized.SentencePracticePopupSentenceCount,
            1,
            10);

        if (!TimeOnly.TryParse(normalized.PauseUntilTomorrowTime, out _))
        {
            throw new ArgumentException("“暂停至明天”的恢复时间格式应为 HH:mm。", nameof(settings));
        }

        if (!QuietHours.TryParse(normalized.QuietHoursStart, normalized.QuietHoursEnd, out _))
        {
            throw new ArgumentException("勿扰时段格式应为 HH:mm。", nameof(settings));
        }

        return normalized;
    }

    private void ApplyDerived(UserSettings settings)
    {
        Scheduler = new SchedulerOptions
        {
            Enabled = settings.RemindersEnabled,
            MinIntervalMinutes = settings.MinIntervalMinutes,
            MaxIntervalMinutes = settings.MaxIntervalMinutes,
            MaxPopupsPerHour = settings.MaxPopupsPerHour,
            QuietHoursStart = settings.QuietHoursStart,
            QuietHoursEnd = settings.QuietHoursEnd,
            IdleThresholdMinutes = settings.IdleThresholdMinutes,
            EnableIdleDetection = settings.EnableIdleDetection,
            DetectFullscreen = settings.DetectFullscreen,
            RecheckDelayMinutes = settings.RecheckDelayMinutes,
        };

        Recommendation = new RecommendationOptions
        {
            NewWordRatio = settings.NewWordRatio,
            ReviewPressureThreshold = _recommendationDefaults.ReviewPressureThreshold,
            OverdueGraceHours = _recommendationDefaults.OverdueGraceHours,
            ReviewCandidateLimit = _recommendationDefaults.ReviewCandidateLimit,
        };

        Card = new CardDisplayOptions
        {
            Width = _cardDefaults.Width,
            EdgeMargin = _cardDefaults.EdgeMargin,
            AutoCloseSeconds = _cardDefaults.AutoCloseSeconds,
            ShowPhonetic = settings.ShowPhonetic,
            AutoExpand = settings.AutoExpand,
            Animate = settings.Animate,
            Theme = settings.Theme,
        };

        QuietHours = settings.GetQuietHours();
    }
}
