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

    // 中文转英文
    /// <summary>启用全局热键 Ctrl+Alt+T，把选中的中文翻译成英文。</summary>
    public bool ChineseToEnglishEnabled { get; set; } = false;

    /// <summary>替换前是否弹出预览窗；关闭后直接替换，可用 Ctrl+Z 撤销。</summary>
    public bool ChineseToEnglishPreviewEnabled { get; set; } = false;

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

    /// <summary>句子练习专用模型；留空则跟随 AiModel。</summary>
    public string SentencePracticeModel { get; set; } = string.Empty;

    /// <summary>多连接配置；旧的单连接字段会迁移为「默认连接」。</summary>
    public List<AiConnection> AiConnections { get; set; } = [];

    /// <summary>手动模式下当前使用的连接 Id。</summary>
    public string ActiveAiConnectionId { get; set; } = AiConnectionSettings.DefaultConnectionId;

    /// <summary>Auto 模式：按列表顺序尝试启用的连接。</summary>
    public bool AiAutoMode { get; set; }

    /// <summary>电脑空闲时后台生成例句。</summary>
    public bool SentenceGenerationEnabled { get; set; } = true;

    /// <summary>每天最多后台生成几组。</summary>
    public int SentenceGenerationRunsPerDay { get; set; } = 2;

    /// <summary>每次生成多少句。</summary>
    public int SentenceGenerationBatchSize { get; set; } = 5;

    /// <summary>空闲多少分钟后才允许后台生成。</summary>
    public int SentenceGenerationIdleMinutes { get; set; } = 5;

    public UserSettings Clone()
    {
        var clone = (UserSettings)MemberwiseClone();
        clone.AiConnections = AiConnections.Select(x => x.Clone()).ToList();
        return clone;
    }

    /// <summary>解析勿扰时段；格式错误时返回禁用。</summary>
    public QuietHours GetQuietHours() =>
        QuietHours.TryParse(QuietHoursStart, QuietHoursEnd, out var quietHours) ? quietHours : QuietHours.Disabled;
}
