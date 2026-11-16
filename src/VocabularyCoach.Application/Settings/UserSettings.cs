using VocabularyCoach.Core.ValueObjects;

namespace VocabularyCoach.Application.Settings;

/// <summary>
/// 用户可在设置界面修改的全部选项。持久化为 JSON（AppSettings 表），
/// 重启后由 <see cref="ISettingsService"/> 加载并覆盖 appsettings.json 默认值。
/// </summary>
public sealed class UserSettings
{
    // 提醒
    public bool RemindersEnabled { get; set; } = true;

    public double MinIntervalMinutes { get; set; } = 12;

    public double MaxIntervalMinutes { get; set; } = 35;

    public int MaxPopupsPerHour { get; set; } = 4;

    public string QuietHoursStart { get; set; } = "22:30";

    public string QuietHoursEnd { get; set; } = "08:00";

    public double IdleThresholdMinutes { get; set; } = 3;

    public bool EnableIdleDetection { get; set; } = true;

    public bool DetectFullscreen { get; set; } = true;

    public double RecheckDelayMinutes { get; set; } = 2;

    // 学习
    public RecommendationMode RecommendationMode { get; set; } = RecommendationMode.Smart;

    /// <summary>“暂停提醒至明天”的恢复时间（HH:mm）。</summary>
    public string PauseUntilTomorrowTime { get; set; } = "00:00";

    public DifficultyMode DifficultyMode { get; set; } = DifficultyMode.Adaptive;

    public int ManualDifficulty { get; set; } = 2;

    public double NewWordRatio { get; set; } = 0.6;

    /// <summary>卡片交互方式：自我评估 / 选择释义 / 混合。</summary>
    public InteractionMode InteractionMode { get; set; } = InteractionMode.SelfReport;

    // 生词捕获
    public bool CaptureEnabled { get; set; } = true;

    public bool CaptureSilentAdd { get; set; } = false;

    /// <summary>捕获后是否显示托盘气泡提示；关闭后静默捕获完全无提示。</summary>
    public bool CaptureShowNotification { get; set; } = true;

    /// <summary>开机自动启动并静默进入托盘。</summary>
    public bool StartWithWindows { get; set; } = false;

    // 显示
    public CardTheme Theme { get; set; } = CardTheme.Light;

    public bool ShowPhonetic { get; set; } = true;

    public bool AutoExpand { get; set; } = false;

    public bool Animate { get; set; } = true;

    // AI（可选增强，Key 单独通过 DPAPI 保存）
    public bool AiEnabled { get; set; } = false;

    public AiPreset AiPreset { get; set; } = AiPreset.OpenAi;

    public string AiBaseUrl { get; set; } = "https://api.openai.com/v1";

    public string AiModel { get; set; } = "gpt-4o-mini";

    public UserSettings Clone() => (UserSettings)MemberwiseClone();

    /// <summary>解析勿扰时段；格式错误时返回禁用。</summary>
    public QuietHours GetQuietHours() =>
        QuietHours.TryParse(QuietHoursStart, QuietHoursEnd, out var quietHours) ? quietHours : QuietHours.Disabled;
}
