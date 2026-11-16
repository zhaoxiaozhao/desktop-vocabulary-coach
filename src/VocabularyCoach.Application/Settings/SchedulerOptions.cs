using VocabularyCoach.Core.ValueObjects;

namespace VocabularyCoach.Application.Settings;

/// <summary>
/// 后台调度参数，绑定 appsettings.json 的 "Scheduler" 节点。
/// 间隔使用分钟（支持小数，便于测试时缩短为秒级）。
/// </summary>
public sealed class SchedulerOptions
{
    public const string SectionName = "Scheduler";

    /// <summary>提醒总开关；关闭时调度器继续运行但不弹卡。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>最短随机间隔（分钟）。</summary>
    public double MinIntervalMinutes { get; set; } = 12;

    /// <summary>最长随机间隔（分钟）。</summary>
    public double MaxIntervalMinutes { get; set; } = 35;

    /// <summary>每小时最多自动弹出次数。</summary>
    public int MaxPopupsPerHour { get; set; } = 4;

    /// <summary>勿扰开始时间，格式 HH:mm。</summary>
    public string QuietHoursStart { get; set; } = "22:30";

    /// <summary>勿扰结束时间，格式 HH:mm。</summary>
    public string QuietHoursEnd { get; set; } = "08:00";

    /// <summary>空闲多少分钟后不再弹出。</summary>
    public double IdleThresholdMinutes { get; set; } = 3;

    public bool EnableIdleDetection { get; set; } = true;

    public bool DetectFullscreen { get; set; } = true;

    /// <summary>状态不允许弹出时的重新检查间隔（分钟）。</summary>
    public double RecheckDelayMinutes { get; set; } = 2;

    /// <summary>解析勿扰时段；格式错误时返回禁用的勿扰设置。</summary>
    public QuietHours GetQuietHours() =>
        QuietHours.TryParse(QuietHoursStart, QuietHoursEnd, out var quietHours) ? quietHours : QuietHours.Disabled;

    /// <summary>勿扰时段配置是否可解析（供启动时校验）。</summary>
    public bool HasValidQuietHours => QuietHours.TryParse(QuietHoursStart, QuietHoursEnd, out _);

    /// <summary>按配置范围生成随机间隔。</summary>
    public TimeSpan NextInterval(Random random)
    {
        var min = Math.Max(0.001, Math.Min(MinIntervalMinutes, MaxIntervalMinutes));
        var max = Math.Max(min, MaxIntervalMinutes);
        var minutes = min + (random.NextDouble() * (max - min));
        return TimeSpan.FromMinutes(minutes);
    }
}
